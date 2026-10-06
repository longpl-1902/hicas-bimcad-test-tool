using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace HicasTest.Bridge.Core
{
    /// <summary>
    /// "invoke" mode: calls a public static method of the add-in under test.
    /// Supported parameters: the host context (UIApplication / Document, matched by type) and one string argument.
    /// </summary>
    public static class ServiceInvoker
    {
        public static string Invoke(string assemblyPath, string typeName, string methodName, object hostContext, string argument)
        {
            if (string.IsNullOrEmpty(assemblyPath) || string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName))
                throw new ArgumentException("invoke mode needs assemblyPath, typeName and methodName.");

            var assembly = Assembly.LoadFrom(assemblyPath);
            var type = assembly.GetType(typeName, true);
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                         ?? throw new MissingMethodException(typeName, methodName);

            return Invoke(method, hostContext, argument);
        }

        /// <summary>Calls a public static method; parameters: the host context (by type) and one string argument.</summary>
        public static string Invoke(MethodInfo method, object hostContext, string argument)
        {
            var parameters = method.GetParameters();
            var args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterType = parameters[i].ParameterType;
                if (parameterType == typeof(string))
                    args[i] = argument;
                else if (parameterType.IsInstanceOfType(hostContext))
                    args[i] = hostContext;
                else
                    throw new ArgumentException("Unsupported parameter '" + parameters[i].Name + "' of type " + parameterType.FullName + ".");
            }

            try
            {
                return method.Invoke(null, args)?.ToString();
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable
            }
        }
    }
}
