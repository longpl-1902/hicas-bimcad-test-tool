using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HicasTest.Protocol;

namespace HicasTest.Bridge.Core
{
    /// <summary>
    /// Finds and calls the test entries of an add-in's test assembly (docs/test-entries.md). Entries are public static
    /// methods marked with an attribute whose full name is HicasTest.Contracts.HicasTestEntryAttribute. They are matched by
    /// name, not by type, so the add-in's copy of the Contracts assembly may be another version than anything here.
    /// </summary>
    public static class EntryInvoker
    {
        private const string AttributeFullName = "HicasTest.Contracts.HicasTestEntryAttribute";
        private const string ContextTypeName = "HicasTest.Contracts.TestContext";
        private const char FieldSeparator = '\u001f';
        private const char ItemSeparator = '\u001e';
        private const int MaxNamesInError = 25;

        /// <summary>Marks this process as a HicasTest host for add-in code (HicasTest.Contracts.TestMode.IsActive).</summary>
        public static void EnableTestMode()
        {
            Environment.SetEnvironmentVariable("HICASTEST_MODE", "1");
        }

        public static EntryListResult List(string assemblyPath)
        {
            var result = new EntryListResult();
            var found = Discover(assemblyPath);
            result.Warnings = ForeignAssemblies(assemblyPath);
            var names = new HashSet<string>(found.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in found.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                var dot = entry.Name.LastIndexOf('.');
                var isStep = dot > 0 && names.Contains(entry.Name.Substring(0, dot));
                result.Entries.Add(new EntryInfo
                {
                    Name = entry.Name,
                    Kind = isStep ? "step" : "main",
                    ReadOnly = entry.ReadOnly,
                    Description = entry.Description,
                    Contract = entry.Contract,
                });
            }
            return result;
        }

        public static EntryCallResult Call(EntryCallRequest request, object hostContext)
        {
            var watch = Stopwatch.StartNew();
            var found = Discover(request.AssemblyPath);
            var entry = found.FirstOrDefault(e => string.Equals(e.Name, request.Name, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                var names = string.Join(", ", found.Select(e => e.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(MaxNamesInError));
                return new EntryCallResult
                {
                    Status = "failed",
                    Error = "No entry '" + request.Name + "' in " + request.AssemblyPath + ". Entries: " + (names.Length == 0 ? "(none)" : names),
                    DurationMs = watch.ElapsedMilliseconds,
                };
            }

            var answers = request.Answers ?? new List<PromptAnswerSpec>();
            var context = entry.ContextType;
            var begin = context == null ? null : context.GetMethod("Begin", new[] { typeof(string[]), typeof(string[]) });
            var end = context == null ? null : context.GetMethod("End", Type.EmptyTypes);
            if (answers.Count > 0 && begin == null)
                return new EntryCallResult
                {
                    Status = "failed",
                    Error = "Answers were given, but the test assembly's HicasTest.Contracts has no TestContext (prompts cannot be scripted).",
                    DurationMs = watch.ElapsedMilliseconds,
                };

            var result = new EntryCallResult { Status = "ok", Warnings = ForeignAssemblies(request.AssemblyPath) };
            try
            {
                if (begin != null)
                    begin.Invoke(null, new object[] { answers.Select(a => a.Id ?? string.Empty).ToArray(), answers.Select(a => a.Option).ToArray() });
                result.Result = ServiceInvoker.Invoke(entry.Method, hostContext, request.Argument);
            }
            catch (Exception ex)
            {
                var root = Unwrap(ex);
                BridgeLog.Error("entry " + request.Name, root);
                result.Status = "failed";
                result.Error = root.GetType().Name + ": " + root.Message;
            }
            finally
            {
                if (end != null)
                {
                    try
                    {
                        result.Prompts = ParsePrompts(end.Invoke(null, null) as string[]);
                    }
                    catch (Exception ex)
                    {
                        BridgeLog.Error("entry prompts " + request.Name, Unwrap(ex));
                    }
                }
                result.DurationMs = watch.ElapsedMilliseconds;
            }
            return result;
        }

        /// <summary>
        /// A .NET assembly loads once per identity: when another build of the add-in (a dev manifest, an installed copy) was loaded
        /// first, the test assembly's references silently bind to THAT code and the entries test the wrong build. Reports every
        /// assembly the build under test ships that is already loaded from another folder.
        /// </summary>
        public static List<string> ForeignAssemblies(string testAssemblyPath)
        {
            var assembly = Assembly.LoadFrom(testAssemblyPath);
            var directory = Path.GetDirectoryName(Path.GetFullPath(testAssemblyPath));
            var loaded = new List<KeyValuePair<string, string>>();
            foreach (var candidate in AppDomain.CurrentDomain.GetAssemblies())
            {
                string location;
                try
                {
                    location = candidate.IsDynamic ? null : candidate.Location;
                }
                catch (NotSupportedException)
                {
                    location = null;
                }
                if (!string.IsNullOrEmpty(location))
                    loaded.Add(new KeyValuePair<string, string>(candidate.GetName().Name, location));
            }

            var referenced = assembly.GetReferencedAssemblies().Select(r => r.Name);
            return FindForeign(referenced, loaded, directory, name => File.Exists(Path.Combine(directory, name + ".dll")));
        }

        // Host and framework assemblies are expected to come from the host, never from the build under test.
        private static readonly string[] HostAssemblyPrefixes =
        {
            "System", "mscorlib", "Microsoft", "netstandard", "WindowsBase", "Presentation", "Accessibility", "UIAutomation",
            "RevitAPI", "AdWindows", "UIFramework", "Autodesk", "Acad", "AcMgd", "AcDbMgd", "AcCoreMgd", "AcWindows",
        };

        public static List<string> FindForeign(IEnumerable<string> referenced, IEnumerable<KeyValuePair<string, string>> loaded,
            string testDirectory, Func<string, bool> shippedWithTestAssembly)
        {
            var problems = new List<string>();
            foreach (var name in referenced.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (HostAssemblyPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) || !shippedWithTestAssembly(name))
                    continue;
                foreach (var copy in loaded.Where(l => string.Equals(l.Key, name, StringComparison.OrdinalIgnoreCase)))
                {
                    var loadedFrom = Path.GetDirectoryName(copy.Value);
                    if (!SameDirectory(loadedFrom, testDirectory))
                        problems.Add(name + " is loaded from " + loadedFrom + ", not from the build under test (" + testDirectory
                                     + "). Another copy of the add-in (a dev manifest, an installed copy) loaded first, so the entries would run that code.");
                }
            }
            return problems;
        }

        private static bool SameDirectory(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                          Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        internal static List<PromptRecord> ParsePrompts(string[] records)
        {
            var prompts = new List<PromptRecord>();
            foreach (var record in records ?? new string[0])
            {
                var fields = record.Split(FieldSeparator);
                if (fields.Length < 7)
                    continue;
                prompts.Add(new PromptRecord
                {
                    Id = fields[1],
                    Severity = fields[2],
                    Message = fields[3],
                    Options = fields[4].Length == 0 ? new List<string>() : fields[4].Split(ItemSeparator).ToList(),
                    Answer = fields[5],
                    Unanswered = fields[6] == "1",
                    Unused = fields[0] == "unused-answer",
                });
            }
            return prompts;
        }

        private static List<Found> Discover(string assemblyPath)
        {
            if (string.IsNullOrEmpty(assemblyPath))
                throw new ArgumentException("entries need the add-in's test assembly (assemblyPath).");

            var assembly = Assembly.LoadFrom(assemblyPath);
            var found = new List<Found>();
            foreach (var type in LoadableTypes(assembly))
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    object attribute = null;
                    foreach (var candidate in method.GetCustomAttributes(false))
                    {
                        if (candidate.GetType().FullName == AttributeFullName)
                        {
                            attribute = candidate;
                            break;
                        }
                    }
                    if (attribute == null)
                        continue;

                    var name = Read<string>(attribute, "Name");
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    if (found.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("Two entries are named '" + name + "' (" + type.FullName + "." + method.Name + ").");

                    found.Add(new Found
                    {
                        Name = name,
                        Description = Read<string>(attribute, "Description"),
                        Contract = Read<string>(attribute, "Contract"),
                        ReadOnly = Read<bool>(attribute, "ReadOnly"),
                        Method = method,
                        ContextType = attribute.GetType().Assembly.GetType(ContextTypeName),
                    });
                }
            }
            return found;
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // A type whose dependency is missing must not hide the entries of the others.
                return ex.Types.Where(t => t != null);
            }
        }

        private static T Read<T>(object attribute, string property)
        {
            var info = attribute.GetType().GetProperty(property);
            if (info == null)
                return default(T);
            var value = info.GetValue(attribute, null);
            return value is T ? (T)value : default(T);
        }

        private static Exception Unwrap(Exception ex)
        {
            var target = ex as TargetInvocationException;
            return target != null && target.InnerException != null ? target.InnerException : ex;
        }

        private sealed class Found
        {
            public string Name;
            public string Description;
            public string Contract;
            public bool ReadOnly;
            public MethodInfo Method;
            public Type ContextType;
        }
    }
}
