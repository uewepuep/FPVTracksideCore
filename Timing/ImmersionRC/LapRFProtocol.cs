/*
 * A decoder/encoder for the ImmersionRC LapRF family of race timing systems
 * This file is part of LapRFCSharpDemo.
 *
 * LapRFCSharpDemo is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * LapRFCSharpDemo is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with LapRFCSharpDemo.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace LapRF
{
	public class LapRFProtocol
	{
		public const int MAX_RXPACKET_LEN = 1024;
		public const int MAX_SLOTS = 16;                // max. number of pilot slots we will ever see

		public enum laprf_type_of_record
		{
			LAPRF_TOR_ERROR = 0xFFFF,

			LAPRF_TOR_RSSI = 0xda01,
			LAPRF_TOR_RFSETUP = 0xda02,
			LAPRF_TOR_STATE_CONTROL = 0xda04,
			LAPRF_TOR_PASSING = 0xda09,
			LAPRF_TOR_SETTINGS = 0xda07,
			LAPRF_TOR_STATUS = 0xda0a,
			LAPRF_TOR_TIME = 0xDA0C,
		};

		public struct laprf_rssi
		{
			public float minRssi;
			public float maxRssi;
			public float meanRssi;
			public float lastRssi;
		};

		public struct laprf_RFSetup
		{
			public UInt16 bEnabled;
			public UInt16 channel;
			public UInt16 band;
			public UInt16 attenuation;
		};

		CRCCalc crcCalc;

		Byte SOR = 0x5a;
		Byte EOR = 0x5b;
		Byte ESC = 0x5c;

		Queue<PassingRecord> passingRecords;

		PassingRecord currentPassingRecord;
		DateTime lastStatusDate;

		MemoryStream dataStream;
		BinaryWriter dataStreamWriter;

		MemoryStream rxStream;
		BinaryWriter rxStreamWriter;

		Boolean inDecode;

		int currentRssiSlot;
		laprf_rssi[] rssiPerSlot = new laprf_rssi[MAX_SLOTS + 1];

		int currentSetupSlot;
		laprf_RFSetup[] rfSetupPerSlot = new laprf_RFSetup[MAX_SLOTS + 1];

		float batteryVoltage;

		int recordCount = 0;
		int recordGoodPackets = 0;
		int recordCRCMismatchCount = 0;

        public DateTime RTCEpoch { get; set; }
        public TimeSpan EpochAccuracy { get; set; }
        public bool EpochOldVersionFix { get; set; }
		private DateTime rtcGetStart;

        //-----------------------------------------------------------------------------------
        public LapRFProtocol()
		{
            RTCEpoch = DateTime.MinValue;
			EpochOldVersionFix = false;

			crcCalc = new CRCCalc();
			passingRecords = new Queue<PassingRecord>();

			inDecode = false;

			currentRssiSlot = 0;
			currentSetupSlot = 0;
            
			// initialize the last status date, assume that we got at least one good status
			lastStatusDate = DateTime.Now;

			// create the Rx stream
			rxStream = new MemoryStream();
			rxStreamWriter = new BinaryWriter(rxStream);

			unitTests();
		}

		//-----------------------------------------------------------------------------------
		// build packet to send out to the gate and fill output buffer
		public void prepare_sendable_packet(laprf_type_of_record recordType)
		{
			dataStream = new MemoryStream();
			dataStreamWriter = new BinaryWriter(dataStream);

			dataStreamWriter.Write((Byte)SOR);
			dataStreamWriter.Write((UInt16)0);           // length placeholder
			dataStreamWriter.Write((UInt16)0);           // CRC placeholder
			dataStreamWriter.Write((UInt16)recordType);   // type of record
		}

		//-----------------------------------------------------------------------------------
		// add Byte field to table
		public void append_field_of_record_u8(Byte signature, Byte data)
		{
			dataStreamWriter.Write((Byte)signature);
			dataStreamWriter.Write((Byte)1);
			dataStreamWriter.Write((Byte)data);
		}

		//-----------------------------------------------------------------------------------
		// add UInt16 field to table
		public void append_field_of_record_u16(Byte signature, UInt16 data)
		{
			dataStreamWriter.Write((Byte)signature);
			dataStreamWriter.Write((Byte)2);
			dataStreamWriter.Write((UInt16)data);
		}

		//-----------------------------------------------------------------------------------
		// add UInt32 field to table
		public void append_field_of_record_u32(Byte signature, UInt32 data)
		{
			dataStreamWriter.Write((Byte)signature);
			dataStreamWriter.Write((Byte)4);
			dataStreamWriter.Write((UInt32)data);
		}

		//-----------------------------------------------------------------------------------
		// add UInt32 field to table
		public void append_field_of_record_u64(Byte signature, UInt64 data)
		{
			dataStreamWriter.Write((Byte)signature);
			dataStreamWriter.Write((Byte)8);
			dataStreamWriter.Write((UInt64)data);
		}

		//-----------------------------------------------------------------------------------
		// add UInt64 field to table
		public void append_field_of_record_u6(Byte signature, UInt64 data)
		{
			dataStreamWriter.Write((Byte)signature);
			dataStreamWriter.Write((Byte)8);
			dataStreamWriter.Write((UInt64)data);
		}

		//-----------------------------------------------------------------------------------
		// add float field to table
		public void append_field_of_record_fl32(Byte signature, float data)
		{
			dataStreamWriter.Write((Byte)signature);
			dataStreamWriter.Write((Byte)4);
			dataStreamWriter.Write((float)data);
		}

		//-----------------------------------------------------------------------------------
		// build packet to send out and fill output buffer
		public MemoryStream finalize_sendable_packet()
		{
			dataStreamWriter.Write((Byte)EOR);
			dataStreamWriter.Seek(1, SeekOrigin.Begin);
			dataStreamWriter.Write((UInt16)dataStream.Length);      // insert length into header

			dataStreamWriter.Seek(3, SeekOrigin.Begin);
			dataStreamWriter.Write((UInt16)0);                     // zero CRC

			UInt16 crcVal = crcCalc.compute_crc16(dataStream.ToArray(), (int)dataStream.Length);
			dataStreamWriter.Seek(3, SeekOrigin.Begin);
			dataStreamWriter.Write((UInt16)crcVal);                 // insert CRC

			dataStream = escape_characters(dataStream);

			Byte[] FinalSend = dataStream.ToArray();

			return dataStream;
		}

		//-----------------------------------------------------------------------------------
		public void processBytes(Byte[] bytes, int numBytes)
		{
			for (int i = 0; i < numBytes; ++i)
				processByte(bytes[i]);
		}

		//-----------------------------------------------------------------------------------
		// search the data sent for a laprf packet, 
		// not the most efficient way to do this, but by far the simplest to get things rolling
		// consider sending a buffer at a time
		//
		// Note that due to 'escaping', the SOR, EOR messages won't appear in the data itself, so
		// we can safely just look for those bytes.
		//
		Boolean processByte(Byte data)
		{
			if (!inDecode)
			{
				if (data == SOR)        // start of record
				{
					inDecode = true;

					rxStream = new MemoryStream();
					rxStreamWriter = new BinaryWriter(rxStream);
					rxStreamWriter.Write((Byte)SOR);
				}
			}
			else
			{
				if (data == EOR)        // end of record
				{
					inDecode = false;
					rxStreamWriter.Write((Byte)EOR);       // don't forget to add the EOR at the end of the packet

					decodePacket();
				}

				rxStreamWriter.Write((Byte)data);
			}

			return true;
		}

		//-----------------------------------------------------------------------------------
		// decode a lapRF packet received from the gate
		//
		void decodePacket()
		{
			int numRecords = 0;

            currentPassingRecord = new PassingRecord();
            currentPassingRecord.bValid = false;

            rxStream = unescapeBuffer(rxStream);
			BinaryReader br = new BinaryReader(rxStream);
			BinaryWriter bw = new BinaryWriter(rxStream);

			// read the header
			//
			rxStream.Seek(0, SeekOrigin.Begin);
			Byte sor = br.ReadByte();
			UInt16 len = br.ReadUInt16();
			UInt16 packetCRC = br.ReadUInt16();        // record the sent CRC before we zero it out to recompute it
			laprf_type_of_record typeOfRecord = (laprf_type_of_record)br.ReadUInt16();

			// zero the CRC before computing it
			//
			rxStream.Seek(3, SeekOrigin.Begin);
			bw.Write((UInt16)0x0000);

			if (sor == SOR && len < MAX_RXPACKET_LEN)
			{
				UInt16 computedCRC = crcCalc.compute_crc16(rxStream.ToArray(), (int)rxStream.Length);

				if (computedCRC == packetCRC)
				{
					++recordGoodPackets;

					// seek just past the header
					rxStream.Seek(7, SeekOrigin.Begin);

					if (typeOfRecord == laprf_type_of_record.LAPRF_TOR_RFSETUP)
					{
						Debug.Print("RFSetup");

						Byte[] ar = rxStream.GetBuffer();
						Debug.Print("{0}", ar.Length);
					}

					// now we can decode the packet contents
					//
					while (decodeRecord(typeOfRecord, br) == true)
					{
						++numRecords;
					}
					++recordCount;
				}
				else
				{
					++recordCRCMismatchCount;
					Debug.Print("CRC Mismatch {0} {1}", computedCRC, packetCRC);
				}
			}
			else
			{
				Debug.Print("bad header");
			}
		}


		//-----------------------------------------------------------------------------------
		// decode a single record within a lapRF packet
		//
		Boolean decodeRecord(laprf_type_of_record typeOfRecord, BinaryReader br)
		{
			UInt32 passingNumber;
			UInt64 rtcTime;
			UInt64 timertc_time;
			Byte pilotId;
			UInt32 minLapTime;

			// read the field of record header
			//
			if (br.BaseStream.Position >= br.BaseStream.Length)
				return false;

			Byte signature = br.ReadByte();
			if (signature == EOR)
				return false;
			Byte numBytes = br.ReadByte();

			int recordLength = 2 + numBytes;        // total size of record

			switch (typeOfRecord)
			{
				case laprf_type_of_record.LAPRF_TOR_PASSING:
                    switch (signature)
                    {
                        case 0x01:                      // re-use transponder ID for slot number
                            pilotId = br.ReadByte();    // 1-based, index 0 not used
                            Debug.Print("LAPRF_TOR_PASSING pilot {0}", pilotId);
                            currentPassingRecord.pilotId = pilotId;
                            currentPassingRecord.bValid = true;                     // not really true...
                            break;

                        case 0x21:
                            if (numBytes == 4)
                            {
                                passingNumber = br.ReadUInt32();
                                currentPassingRecord.passingNumber = passingNumber;
                                Debug.Print("LAPRF_TOR_PASSING # {0}", passingNumber);
                                currentPassingRecord.bValid = true;                 // not really true...
                            }
                            break;

                        case 0x02:          // RTC_TIME
                            if (numBytes == 8)
                            {
                                rtcTime = br.ReadUInt64();
                                currentPassingRecord.rtcTime = rtcTime;
                                Debug.Print("LAPRF_TOR_PASSING time {0} {1}", rtcTime, GetTime(rtcTime / 1000).ToString("MMMM dd, yyyy - H:mm:ss.fff"));
                                currentPassingRecord.bValid = true;                     // not really true...

                                // store the passing record in a queue (FIFO)
                                passingRecords.Enqueue(currentPassingRecord);
                            }
                            break;
                        case 0x22: // DETECTION_PEAK_HEIGHT 
                            if (numBytes == 2)
                            {
                                ushort peak = br.ReadUInt16();
                                currentPassingRecord.peak = peak;
                            }
                            break;
                    }
					break;

				case laprf_type_of_record.LAPRF_TOR_STATUS:
					lastStatusDate = DateTime.Now;

					switch (signature)
					{
						case 0x01:                                  // re-use transponder ID for slot number
							currentRssiSlot = br.ReadByte();        // 1-based, index 0 not used
							Debug.Print("LAPRF_TOR_STATUS {0}", currentRssiSlot);
							break;

                        case 0x03:                                  // status flags
							UInt16 dummy = br.ReadUInt16();
							break;

						case 0x21:          // INPUT_VOLTAGE
							if (numBytes == 2)
							{
								UInt16 voltagemV = br.ReadUInt16();
								batteryVoltage = voltagemV / 1000.0f;

								Debug.Print("LAPRF_TOR_STATUS voltage {0}", voltagemV);
							}
							break;

						case 0x22:          // Instantaneous RSSI
							if (numBytes == 4)
							{
								float rssiLevel = br.ReadSingle();
								rssiPerSlot[currentRssiSlot].lastRssi = rssiLevel;
								Debug.Print("LAPRF_TOR_STATUS rssi {0}", rssiLevel);
							}
							break;

						case 0x23:          // Gate State
							if (numBytes == 1)
							{
								Byte gateState = br.ReadByte();
								Debug.Print("GateState det # {0}", gateState);
							}
							break;

						case 0x24:          // Number of detections (passing records)
							if (numBytes == 4)
							{
								UInt32 detectionCount = br.ReadUInt32();
								Debug.Print("LAPRF_TOR_STATUS det # {0}", detectionCount);
							}
							break;

					}
					break;

				case laprf_type_of_record.LAPRF_TOR_RSSI:
					switch (signature)
					{
						case 0x01:                      // re-use transponder ID for slot number
							currentRssiSlot = br.ReadByte();       // 1-based, index 0 not used
							Debug.Print("LAPRF_TOR_RSSI {0}", currentRssiSlot);
							break;

						case 0x20:                      // min rssi, 4 byte float
							rssiPerSlot[currentRssiSlot].minRssi = br.ReadSingle();
							Debug.Print(" min  {0}", rssiPerSlot[currentRssiSlot].minRssi);
							break;

						case 0x21:                      // max rssi, 4 byte float
							rssiPerSlot[currentRssiSlot].maxRssi = br.ReadSingle();
							Debug.Print(" max  {0}", rssiPerSlot[currentRssiSlot].maxRssi);
							break;

						case 0x22:                      // mean rssi, 4 byte float
							rssiPerSlot[currentRssiSlot].meanRssi = br.ReadSingle();
							Debug.Print(" mean {0}", rssiPerSlot[currentRssiSlot].meanRssi);
							break;

						case 0x07:                      // sample count, 4 byte int
														// TBD
							break;
					}
					break;

				case laprf_type_of_record.LAPRF_TOR_RFSETUP:                // read and write
					switch (signature)
					{
						case 0x01:                      // re-use transponder ID for slot number
							currentSetupSlot = br.ReadByte();      // 1-based, index 0 not used
							if (currentSetupSlot > MAX_SLOTS)
								currentSetupSlot = 0;                       // default to zero if confused
							Debug.Print("LAPRF_TOR_RFSETUP {0}", currentSetupSlot);
							break;

						case 0x20:                      // Enabled
							rfSetupPerSlot[currentSetupSlot].bEnabled = br.ReadUInt16();
							Debug.Print("   enabled {0}", rfSetupPerSlot[currentSetupSlot].bEnabled);
							break;

						case 0x21:                      // Channel
							rfSetupPerSlot[currentSetupSlot].channel = br.ReadUInt16();
							break;

						case 0x22:                      // Band
							rfSetupPerSlot[currentSetupSlot].band = br.ReadUInt16();
							break;

						case 0x24:                      // Attenuation
							rfSetupPerSlot[currentSetupSlot].attenuation = br.ReadUInt16();
							break;

						case 0x25:                      // Frequency
														//rfSetupPerSlot[currentSetupSlot].frequency = br.ReadUInt16();
							Debug.Print("   freq {0}", br.ReadUInt16());

							break;

					}
					break;

				case laprf_type_of_record.LAPRF_TOR_SETTINGS:                               // read and write
					switch (signature)
					{
						case 0x26:                                  // re-use transponder ID for slot number
							minLapTime = br.ReadUInt32();
							Debug.Print("LAPRF_TOR_SETTINGS minLapTime {0}", minLapTime);
							break;
					}
					break;

				case laprf_type_of_record.LAPRF_TOR_TIME:                                   // read and write
					switch (signature)
					{
						case 0x02:
                            DateTime now = DateTime.Now;
                            rtcTime = br.ReadUInt64();
							Debug.Print("LAPRF_TOR_TIME RTC_TIME {0} {1}", rtcTime, GetTime(rtcTime).ToString("MMMM dd, yyyy - H:mm:ss"));

							double ms = rtcTime / 1000.0;
							
							if (EpochOldVersionFix)
							{
								ms = rtcTime;
							}
							
							RTCEpoch = now.AddMilliseconds(-ms);
                            EpochAccuracy = now - rtcGetStart;

                            Tools.Logger.TimingLog.Log(this, "Calculated Epoch", string.Join(", ", rtcTime, RTCEpoch, EpochAccuracy.TotalSeconds), Tools.Logger.LogType.Notice);
                            break;


						case 0x20:
							timertc_time = br.ReadUInt64();
							Debug.Print("LAPRF_TOR_TIME TIME_RTC_TIME {0}", timertc_time);
							break;
					}
					break;
			}

			return true;
		}

		//-----------------------------------------------------------------------------------
		public DateTime GetTime(ulong ms)
		{
			DateTime pointOfReference = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			//pointOfReference.AddMilliseconds(ms).ToLocalTime();
			return pointOfReference.AddMilliseconds(ms).ToLocalTime();
		}

		//-----------------------------------------------------------------------------------
		// reverse the escape_characters process
		MemoryStream unescapeBuffer(MemoryStream streamIn)
		{
			Byte[] dataIn = streamIn.ToArray();
			long lengthIn = streamIn.Length;

			MemoryStream dataOut = new MemoryStream();
			BinaryWriter dataOutWriter = new BinaryWriter(dataOut);

			UInt32 iInIdx = 0;

			while (iInIdx < lengthIn)
			{
				if (dataIn[iInIdx] == ESC)                                   // escape character
				{
					++iInIdx;
					dataOutWriter.Write((Byte)(dataIn[iInIdx] - 0x40));    // next char - 40
				}
				else
					dataOutWriter.Write((Byte)(dataIn[iInIdx]));            // next char - 40

				++iInIdx;
			}

			return dataOut;
		}

		//-----------------------------------------------------------------------------------
		// modify the buffer contents to replace ESC, SOR, and EOR with escaped versions
		MemoryStream escape_characters(MemoryStream streamIn)
		{
			Byte[] dataIn = streamIn.ToArray();
			long lengthIn = streamIn.Length;

			MemoryStream dataOut = new MemoryStream();
			BinaryWriter dataOutWriter = new BinaryWriter(dataOut);

			for (int i = 0; i < lengthIn; i++)
			{
				if ((dataIn[i] == ESC ||  // ESC
					 dataIn[i] == SOR ||  // SOR
					 dataIn[i] == EOR) && (i != 0) && (i != (lengthIn - 1))        // EOR
					)
				{
					dataOutWriter.Write((Byte)ESC);
					dataOutWriter.Write((Byte)(dataIn[i] + 0x40));
				}
				else
				{
					dataOutWriter.Write((Byte)dataIn[i]);
				}
			}

			return dataOut;
		}

		//-----------------------------------------------------------------------------------
		string hex_encode(MemoryStream streamIn)
		{
			Byte[] dataIn = streamIn.ToArray();
			long lengthIn = streamIn.Length;
			string outString = "";

			for (int i = 0; i < lengthIn; ++i)
			{
				String hexVal = dataIn[i].ToString("X2");
				outString += hexVal;
			}

			return outString;
		}

		//-----------------------------------------------------------------------------------
		// set the enable/frequency of the specified pilot slot
		public MemoryStream setPilotSlot(int iPilot, Boolean enable, UInt16 freqMHz)
		{
			Debug.Print("setPilotSlot: {0} {1} {2}", iPilot, enable, freqMHz);

			prepare_sendable_packet(laprf_type_of_record.LAPRF_TOR_RFSETUP);
			append_field_of_record_u8(0x01, (Byte)iPilot);
			if (enable)
				append_field_of_record_u16(0x20, 0x01);
			else
				append_field_of_record_u16(0x20, 0x00);
			append_field_of_record_u16(0x25, freqMHz);

			return finalize_sendable_packet();
		}

		//-----------------------------------------------------------------------------------
		// set the interval at which the status messages are streamed from the LapRF
		public MemoryStream setStatusMessageInterval(UInt16 timems)
		{
			prepare_sendable_packet(laprf_type_of_record.LAPRF_TOR_SETTINGS);
			append_field_of_record_u16(0x22, timems);
			return finalize_sendable_packet();
		}

		//-----------------------------------------------------------------------------------
		// set the interval at which the RSSI messages are streamed from the LapRF
		public MemoryStream setRSSIPacketRate(UInt32 intervalMs)
		{
			prepare_sendable_packet(laprf_type_of_record.LAPRF_TOR_RSSI);

			if (intervalMs == 0)
			{
				append_field_of_record_u8(0x24, 0x00);               // RSSI_ENABLE
				append_field_of_record_u32(0x25, 1000);              // RSSI_STAT_INTERVAL (default to 1000)
			}
			else
			{
				append_field_of_record_u8(0x24, 0x01);               // RSSI_ENABLE
				append_field_of_record_u32(0x25, intervalMs);        // RSSI_STAT_INTERVAL
			}

			return finalize_sendable_packet();
		}

		//-----------------------------------------------------------------------------------
		// request the RF setup packets from all slots from the gate
		public MemoryStream requestRTCTime()
		{
            Tools.Logger.TimingLog.Log(this, "Request RTC Time", Tools.Logger.LogType.Notice);

            prepare_sendable_packet(laprf_type_of_record.LAPRF_TOR_TIME);
			dataStreamWriter.Write((Byte)0x02); // RTC_TIME
			dataStreamWriter.Write((Byte)0);

            rtcGetStart = DateTime.Now;
            RTCEpoch = DateTime.MinValue;

            return finalize_sendable_packet();
		}

		//-----------------------------------------------------------------------------------
		// request the RF setup packets from all slots from the gate
		public MemoryStream requestRFSetup()
		{
			prepare_sendable_packet(laprf_type_of_record.LAPRF_TOR_RFSETUP);
			for (int i = 1; i <= 8; ++i)
				append_field_of_record_u8(0x01, (Byte)i);

			return finalize_sendable_packet();
		}

		//-----------------------------------------------------------------------------------
		public laprf_rssi getRssiPerSlot(int iSlot)
		{
			return rssiPerSlot[iSlot];
		}

		//-----------------------------------------------------------------------------------
		public laprf_RFSetup getRFSetupPerSlot(int iSlot)
		{
			return rfSetupPerSlot[iSlot];
		}

		//-----------------------------------------------------------------------------------
		public float getBatteryVoltage()
		{
			return batteryVoltage;
		}

		//-----------------------------------------------------------------------------------
		public void clearPassingRecords()
		{
			passingRecords.Clear();
		}

		//-----------------------------------------------------------------------------------
		public int getPassingRecordCount()
		{
			return passingRecords.Count;
		}

		//-----------------------------------------------------------------------------------
		public PassingRecord getNextPassingRecord()
		{
			return passingRecords.Dequeue();
		}

		//-----------------------------------------------------------------------------------
		// unit testing
		void unitTests()
		{
			// some internal unit testing
			prepare_sendable_packet(laprf_type_of_record.LAPRF_TOR_RFSETUP);
			append_field_of_record_u16(0x01, 0x0000);       // transponder 0 = send all records
			finalize_sendable_packet();

			// test Escape/Unescape mechanism
			//
			MemoryStream testStream = new MemoryStream();
			BinaryWriter testStreamWriter = new BinaryWriter(testStream);
			testStreamWriter.Write((Byte)0x01);
			testStreamWriter.Write((Byte)0x5c);
			testStreamWriter.Write((Byte)0x01);
			testStream = escape_characters(testStream);
			Byte[] FinalSend = testStream.ToArray();

			testStream = unescapeBuffer(testStream);
			FinalSend = testStream.ToArray();

			Debug.Print(hex_encode(testStream));

			Byte[] bytes = { 0x5a, 0x3d, 0x00, 0x00, 0x00, 0x0a, 0xda, 0x21, 0x02,
							 0x3c, 0x0d, 0x23, 0x01, 0x01, 0x24, 0x04, 0x00, 0x00,
							 0x00, 0x00, 0x01, 0x01, 0x01, 0x22, 0x04, 0x00, 0x80,
							 0x62, 0x44, 0x01, 0x01, 0x02, 0x22, 0x04, 0x00, 0x00,
							 0x62, 0x44, 0x01, 0x01, 0x03, 0x22, 0x04, 0x00, 0x80,
							 0x6a, 0x44, 0x01, 0x01, 0x04, 0x22, 0x04, 0x00, 0x00,
							 0x62, 0x44, 0x03, 0x02, 0x00, 0x00, 0x5b};

			// test the decoder
			for (int i = 0; i < bytes.Length; ++i)
				processByte(bytes[i]);
		}

	}
}