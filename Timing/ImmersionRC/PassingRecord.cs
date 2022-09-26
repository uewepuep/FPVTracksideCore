/*
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

public class PassingRecord
{
    public Boolean bValid;
    public UInt32 passingNumber;
    public Byte pilotId;                        // pilot ID, from 1 to max pilots
    public UInt32 transponderId;
    public UInt32 timestamp;
    public UInt64 rtcTime;                      // in microseconds
    public UInt16 peak;                      

    public PassingRecord()
    {
 
    }

    Byte getSlotNumber()
    {
        return pilotId;
    }

    Int16 getFrequency()
    {
        return 0;       //transponderId % 10000;               // right-most 4 digits of the transponder ID
    }

    UInt64 getTime()
    {
        return rtcTime;
    }

    // return time in seconds between the two times
    float getDeltaTime(UInt64 reference)
    {
        Int64 microSecDelta = (Int64) (rtcTime - reference);

        return (float) (microSecDelta / 1e6);
    }      
    
}
