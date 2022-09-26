using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace ImageServer
{
    public static class Ext
    {
        public static string ToHumanString(this Splits split)
        {
            switch (split)
            {
                default:
                case Splits.Custom:
                case Splits.SingleChannel:
                    return split.ToString().CamelCaseToHuman();
                case Splits.TwoByTwo:
                    return "2 x 2";
                case Splits.ThreeByTwo:
                    return "3 x 2";
                case Splits.ThreeByThree:
                    return "3 x 3";
                case Splits.FourByFour:
                    return "4 x 4";
            }
        }

    }
}
