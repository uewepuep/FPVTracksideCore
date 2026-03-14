using DirectShowLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.DirectShow
{
    public static class DirectShowHelper
    {
        public static IEnumerable<DsDevice> VideoCaptureDevices
        {
            get
            {
                DsDevice[] capDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
                foreach (DsDevice ds in capDevices)
                {
                    yield return ds;
                }
            }
        }

        public static IEnumerable<DsDevice> AudioCaptureDevices
        {
            get
            {
                DsDevice[] capDevices = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice);
                foreach (DsDevice ds in capDevices)
                {
                    yield return ds;
                }
            }
        }

        public static DsDevice GetVideoCaptureDeviceByPath(string path)
        {
            DsDevice[] devices = VideoCaptureDevices.ToArray();
            DsDevice chosen = devices.FirstOrDefault(c => c.DevicePath == path);

            foreach (DsDevice dsDevice in devices)
            {
                if (dsDevice != chosen)
                {
                    dsDevice.Dispose();
                }
            }

            return chosen;
        }

        public static DsDevice GetVideoCaptureDeviceByName(string name)
        {
            DsDevice[] devices = VideoCaptureDevices.ToArray();

            DsDevice chosen = null;

            IEnumerable<DsDevice> matches = devices.Where(c => c.Name == name);
            if (matches.Count() == 1)
            {
                chosen = matches.First();
            }

            foreach (DsDevice dsDevice in devices)
            {
                if (dsDevice != chosen)
                {
                    dsDevice.Dispose();
                }
            }

            return chosen;
        }

        public static DsDevice GetAudioCaptureDevice(string path)
        {
            DsDevice[] devices = AudioCaptureDevices.ToArray();
            DsDevice chosen = devices.FirstOrDefault(c => c.DevicePath == path);

            foreach (DsDevice dsDevice in devices)
            {
                if (dsDevice != chosen)
                {
                    dsDevice.Dispose();
                }
            }

            return chosen;
        }

        public static IPin GetPin(this IBaseFilter filter, PinDirection direction, string name = null)
        {
            IPin[] pins = filter.GetPins();

            IPin output = null;
            foreach (IPin p in pins)
            {
                string pName = p.Name();
                PinDirection pDirection = p.Direction();

                if (pDirection == direction && (string.IsNullOrEmpty(name) || pName == name))
                {
                    output = p;
                    break;
                }
                else
                {
                    Marshal.ReleaseComObject(p);
                }
            }
            return output;
        }

        public static IPin[] GetPins(this IBaseFilter filter)
        {
            List<IPin> outPins = new List<IPin>();
            if (filter == null) throw new ArgumentNullException("filter");
            int hr = 0;

            IEnumPins pinsEnum = null;
            try
            {
                hr = filter.EnumPins(out pinsEnum);
                DsError.ThrowExceptionForHR(hr);

                if (pinsEnum == null) throw new InvalidOperationException("pinsEnum is null");

                var pins = new IPin[1];

                while (true)
                {
                    int fetched = 0;

                    IntPtr pcFetched = Marshal.AllocCoTaskMem(4);
                    try
                    {
                        hr = pinsEnum.Next(pins.Length, pins, pcFetched);
                        DsError.ThrowExceptionForHR(hr);
                        fetched = Marshal.ReadInt32(pcFetched);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pcFetched);
                    }

                    if (fetched == 1)
                    {
                        if (!pins[0].Connected())
                        {
                            // we have something
                            outPins.Add(pins[0]);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                if (pinsEnum != null) Marshal.ReleaseComObject(pinsEnum);
            }

            return outPins.ToArray();
        }


        public static AMMediaType[] GetMediaTypes(this IPin pin)
        {
            List<AMMediaType> outPins = new List<AMMediaType>();
            if (pin == null) throw new ArgumentNullException("pin");
            int hr = 0;

            IEnumMediaTypes mediaEnum = null;
            try
            {
                hr = pin.EnumMediaTypes(out mediaEnum);
                DsError.ThrowExceptionForHR(hr);

                if (mediaEnum == null) throw new InvalidOperationException("mediaEnum is null");

                var types = new AMMediaType[1];

                while (true)
                {
                    int fetched = 0;

                    IntPtr pcFetched = Marshal.AllocCoTaskMem(4);
                    try
                    {
                        hr = mediaEnum.Next(types.Length, types, pcFetched);
                        DsError.ThrowExceptionForHR(hr);
                        fetched = Marshal.ReadInt32(pcFetched);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pcFetched);
                    }

                    if (fetched == 1)
                    {
                        outPins.Add(types[0]);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                if (mediaEnum != null) Marshal.ReleaseComObject(mediaEnum);
            }

            return outPins.ToArray();
        }

        public static string Name(this IPin p)
        {
            int hr;
            PinInfo pinInfo;

            hr = p.QueryPinInfo(out pinInfo);
            DsError.ThrowExceptionForHR(hr);
            return pinInfo.name;
        }

        public static PinDirection Direction(this IPin p)
        {
            int hr;
            PinInfo pinInfo;
            hr = p.QueryPinInfo(out pinInfo);
            DsError.ThrowExceptionForHR(hr);
            return pinInfo.dir;
        }

        public static bool Connected(this IPin p)
        {
            int hr;

            IPin other;
            hr = p.ConnectedTo(out other);
            return hr == 0;
        }

        public static int ConnectPin(this IGraphBuilder gb, IBaseFilter source, IBaseFilter dest, string destPinName)
        {
            return gb.ConnectPin(source, null, dest, destPinName, null);
        }

        public static int ConnectPin(this IGraphBuilder gb, IBaseFilter source, IBaseFilter dest, AMMediaType mediaType = null)
        {
            return gb.ConnectPin(source, null, dest, null, null);
        }

        public static int ConnectPin(this IGraphBuilder gb, IBaseFilter source, string sourcePinName, IBaseFilter dest, string destPinName = null, AMMediaType mediaType = null)
        {
            IPin sourcePin = source.GetPin(PinDirection.Output, sourcePinName);
            IPin destPin = dest.GetPin(PinDirection.Input, destPinName);

            return gb.ConnectPin(sourcePin, destPin);
        }

        private static int ConnectPin(this IGraphBuilder gb, IPin sourcePin, IPin destPin, bool directConnect = false, AMMediaType mediaType = null)
        {
            int hr = 0;
            try
            {
                if (directConnect)
                {
                    hr = gb.ConnectDirect(sourcePin, destPin, mediaType);
                    DsError.ThrowExceptionForHR(hr);
                }
                else
                {
                    hr = gb.Connect(sourcePin, destPin);
                    DsError.ThrowExceptionForHR(hr);
                }
            }
            catch (Exception e)
            {
                throw new Exception("Couldn't join '" + sourcePin.Name() + "' with '" + destPin.Name() + "'", e);
            }
            finally
            {
                Marshal.ReleaseComObject(sourcePin);
                Marshal.ReleaseComObject(destPin);
            }
            return hr;
        }

        public static IBaseFilter AddFilter(this IGraphBuilder gb, Guid category, string name, string graphName = null)
        {
            int hr;
            IBaseFilter filter = DirectShowHelper.GetFilter(category, name);

            if (string.IsNullOrEmpty(graphName))
            {
                hr = gb.AddFilter(filter, name);
            }
            else
            {
                hr = gb.AddFilter(filter, graphName);
            }
            DsError.ThrowExceptionForHR(hr);
            return filter;
        }


        public static IBaseFilter GetFilter(Guid category, string name)
        {
            object source = null;
            Guid iid = typeof(IBaseFilter).GUID;
            DsDevice[] list = DsDevice.GetDevicesOfCat(category);
            foreach (DsDevice device in list)
            {
                if (device.Name.Contains(name))
                {
                    device.Mon.BindToObject(null, null, ref iid, out source);
                    return (IBaseFilter)source;
                }
            }

            throw new Exception(string.Join("\n", list.OrderBy(d => d.Name).Select(d => d.Name)));
        }
    }
}
