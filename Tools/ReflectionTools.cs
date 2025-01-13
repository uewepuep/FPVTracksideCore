using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    public static class ReflectionTools
    {
        public static void Copy<Source, Destination>(Source[] source, out Destination[] destination) where Destination : new()
        {
            if (source == null)
            {
                destination = null;
                return;
            }

            destination = new Destination[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = new Destination();
                Copy(source[i], destination[i]);
            }
        }

        public static void Copy<Source, Destination>(Source source, Destination destination)
        {
            if (source == null || destination == null)
                return;

            Type typeSrc = source.GetType();
            Type typeDest = destination.GetType();

            // Iterate the Properties of the source instance and  
            // populate them from their desination counterparts  
            PropertyInfo[] srcProps = typeSrc.GetProperties();

            foreach (PropertyInfo srcProp in srcProps)
            {
                if (!srcProp.CanRead)
                {
                    continue;
                }
                PropertyInfo targetProperty = typeDest.GetProperty(srcProp.Name);
                if (targetProperty == null)
                {
                    continue;
                }
                if (!targetProperty.CanWrite)
                {
                    continue;
                }
                if (targetProperty.GetSetMethod(true) != null && targetProperty.GetSetMethod(true).IsPrivate)
                {
                    continue;
                }
                if ((targetProperty.GetSetMethod().Attributes & MethodAttributes.Static) != 0)
                {
                    continue;
                }
                object? value = srcProp.GetValue(source, null);

                if (value != null)
                {
                    if (targetProperty.PropertyType.IsEnum)
                    {
                        IEnumerable<Enum> valid = Enum.GetValues(targetProperty.PropertyType).OfType<Enum>().Where(e => e.ToString() == value.ToString());
                        if (!valid.Any())
                        {
                            continue;
                        }

                        value = valid.First();
                    }
                    else if (srcProp.PropertyType.IsEnum && targetProperty.PropertyType == typeof(string))
                    {
                        value = value.ToString();
                    }
                    else if (!targetProperty.PropertyType.IsAssignableFrom(srcProp.PropertyType))
                    {
                        continue;
                    }
                }

                // Passed all tests, lets set the value
                targetProperty.SetValue(destination, value, null);
            }
        }
    }
}
