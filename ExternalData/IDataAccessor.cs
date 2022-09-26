using System;
using System.Collections.Generic;
using System.Linq;

namespace ExternalData
{
    public delegate void CallBack<T>(T data, Exception e);
    public delegate void CallBacks<T>(IEnumerable<T> data, Exception e);

    public interface IDataAccessor
	{
        void GetObject<T>(IRequest request, CallBack<T> acceptObjects);
        T GetObject<T>(IRequest command);
	}
}

