using System.Globalization;
using Autodesk.Revit.DB;

namespace HicasTest.Bridge.Revit
{
    /// <summary>ElementId as a wire string across Revit years (Value/long from 2024, IntegerValue/int before).</summary>
    internal static class ElementIds
    {
        public static long Number(ElementId id)
        {
#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        public static string Key(ElementId id) => Number(id).ToString(CultureInfo.InvariantCulture);

        public static ElementId Parse(string key)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(long.Parse(key, CultureInfo.InvariantCulture));
#else
            return new ElementId(int.Parse(key, CultureInfo.InvariantCulture));
#endif
        }
    }
}
