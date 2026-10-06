using System;
using System.Collections.Generic;
using System.Text;

namespace HicasTest.Contracts
{
    /// <summary>
    /// Prompts of a feature as data. The add-in's test implementation of its user-prompt interface calls
    /// <see cref="Ask"/>: during a test entry call it answers from the answers the caller scripted (or the default)
    /// and records the prompt. The bridge starts and ends the call context through <see cref="Begin"/> / <see cref="End"/>
    /// (by reflection, so it does not depend on this assembly).
    /// </summary>
    public static class TestContext
    {
        public const char FieldSeparator = '\u001f';
        public const char ItemSeparator = '\u001e';

        // An entry runs synchronously on the host's main thread, from Begin to End.
        [ThreadStatic]
        private static Session _current;

        public static bool IsActive
        {
            get { return _current != null; }
        }

        /// <summary>Starts a call with scripted answers (prompt id → option).</summary>
        public static void Begin(string[] answerIds, string[] answerOptions)
        {
            var session = new Session();
            if (answerIds != null)
            {
                for (var i = 0; i < answerIds.Length; i++)
                    session.Answers[answerIds[i] ?? string.Empty] = answerOptions != null && i < answerOptions.Length ? answerOptions[i] : null;
            }
            _current = session;
        }

        /// <summary>
        /// Ends the call and returns one record per prompt raised, plus one per scripted answer that was never used.
        /// Record: kind, id, severity, message, options (joined by <see cref="ItemSeparator"/>), answer, unanswered (1/0),
        /// fields joined by <see cref="FieldSeparator"/>.
        /// </summary>
        public static string[] End()
        {
            var session = _current;
            _current = null;
            if (session == null)
                return new string[0];

            var records = new List<string>(session.Records);
            foreach (var pair in session.Answers)
            {
                if (!session.Used.Contains(pair.Key))
                    records.Add(Format("unused-answer", pair.Key, string.Empty, string.Empty, null, pair.Value, false));
            }
            return records.ToArray();
        }

        /// <summary>
        /// Raises a prompt. Returns the scripted answer for <paramref name="id"/> or, when none was scripted,
        /// <paramref name="defaultOption"/> (recorded as unanswered). Outside a test call it just returns the default.
        /// </summary>
        public static string Ask(string id, string severity, string message, string[] options, string defaultOption)
        {
            var session = _current;
            if (session == null)
                return defaultOption;

            string answer;
            var scripted = session.Answers.TryGetValue(id ?? string.Empty, out answer);
            if (scripted)
            {
                session.Used.Add(id ?? string.Empty);
                if (options != null && options.Length > 0 && Array.FindIndex(options, o => string.Equals(o, answer, StringComparison.OrdinalIgnoreCase)) < 0)
                    throw new ArgumentException("Scripted answer '" + answer + "' for prompt '" + id + "' is not one of: " + string.Join(", ", options) + ".");
            }
            else
            {
                answer = defaultOption;
            }

            session.Records.Add(Format("prompt", id, severity, message, options, answer, !scripted));
            return answer;
        }

        private static string Format(string kind, string id, string severity, string message, string[] options, string answer, bool unanswered)
        {
            var text = new StringBuilder();
            text.Append(kind).Append(FieldSeparator)
                .Append(id).Append(FieldSeparator)
                .Append(severity).Append(FieldSeparator)
                .Append(message).Append(FieldSeparator)
                .Append(options == null ? string.Empty : string.Join(ItemSeparator.ToString(), options)).Append(FieldSeparator)
                .Append(answer).Append(FieldSeparator)
                .Append(unanswered ? "1" : "0");
            return text.ToString();
        }

        private sealed class Session
        {
            public readonly Dictionary<string, string> Answers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Records = new List<string>();
        }
    }
}
