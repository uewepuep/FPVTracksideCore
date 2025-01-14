using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExternalData
{
    public interface IRequest
    { 
        [Newtonsoft.Json.JsonIgnore]
        string URL { get;}
    }

    public class Response
    {
        public bool Success { get { return Code == 100; } }
        public string Error { get; set; }
        public int Code { get; set; }

#if DEBUG
        public string Contents { get; set; }
#endif

        public override string ToString()
        {
            return Code.ToString() + " " + Error.ToString();    
        }
    }
}
