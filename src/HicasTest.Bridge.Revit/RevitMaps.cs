using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace HicasTest.Bridge.Revit
{
    internal static class RevitMaps
    {
        private static readonly Dictionary<string, ForgeTypeId> Units = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            ["mm"] = UnitTypeId.Millimeters,
            ["cm"] = UnitTypeId.Centimeters,
            ["m"] = UnitTypeId.Meters,
            ["ft"] = UnitTypeId.Feet,
            ["in"] = UnitTypeId.Inches,
            ["m2"] = UnitTypeId.SquareMeters,
            ["ft2"] = UnitTypeId.SquareFeet,
            ["m3"] = UnitTypeId.CubicMeters,
            ["deg"] = UnitTypeId.Degrees,
        };

        // TaskDialogResult values accepted by DialogBoxShowingEventArgs.OverrideResult.
        private static readonly Dictionary<string, int> Answers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = 1,
            ["cancel"] = 2,
            ["retry"] = 4,
            ["yes"] = 6,
            ["no"] = 7,
            ["close"] = 8,
            ["commandlink1"] = 1001,
            ["commandlink2"] = 1002,
            ["commandlink3"] = 1003,
            ["commandlink4"] = 1004,
        };

        public static ForgeTypeId Unit(string unit)
        {
            if (Units.TryGetValue(unit, out var id))
                return id;
            throw new ArgumentException("Unsupported unit '" + unit + "'. Use one of: " + string.Join(", ", Units.Keys));
        }

        public static bool TryAnswer(string answer, out int result) => Answers.TryGetValue(answer ?? string.Empty, out result);

        public static BuiltInCategory Category(string name)
        {
            if (Enum.TryParse(name, true, out BuiltInCategory category))
                return category;
            throw new ArgumentException("Unknown BuiltInCategory '" + name + "'.");
        }

        public static string CategoryName(Element element)
        {
            var category = element?.Category;
            return category == null ? null : ((BuiltInCategory)ElementIds.Number(category.Id)).ToString();
        }
    }
}
