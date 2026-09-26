using System;
using System.Collections.Generic;
using System.Reflection;

namespace TabCycler.Tests
{
    /// <summary>Marks a method as a test. The name is what gets reported.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute
    {
        public readonly string Name;
        public TestAttribute(string name) { Name = name; }
    }

    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    /// <summary>
    /// Assertions, kept deliberately tiny. No external test package, because
    /// this machine has no .NET SDK and the build has to work from the .NET
    /// Framework compiler that ships with Windows alone.
    /// </summary>
    public static class Assert
    {
        public static void True(bool condition, string because)
        {
            if (!condition) throw new AssertionException("expected true: " + because);
        }

        public static void False(bool condition, string because)
        {
            if (condition) throw new AssertionException("expected false: " + because);
        }

        public static void Equal(object expected, object actual, string because)
        {
            if (!object.Equals(expected, actual))
                throw new AssertionException(
                    "expected [" + expected + "] but got [" + actual + "] : " + because);
        }

        public static void Same(WatchState expected, WatchState actual, string because)
        {
            if (expected != actual)
                throw new AssertionException(
                    "expected state " + expected + " but got " + actual + " : " + because);
        }

        public static void Throws<T>(Action action, string because) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertionException(
                    "expected " + typeof(T).Name + " but got " + ex.GetType().Name + " : " + because);
            }
            throw new AssertionException("expected " + typeof(T).Name + ", nothing was thrown : " + because);
        }
    }

    public static class TestRunner
    {
        public static int Run(string filter)
        {
            var failures = new List<string>();
            int passed = 0, skipped = 0;

            foreach (Type type in typeof(TestRunner).Assembly.GetTypes())
            {
                MethodInfo[] methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly);

                foreach (MethodInfo m in methods)
                {
                    object[] attrs = m.GetCustomAttributes(typeof(TestAttribute), false);
                    if (attrs.Length == 0) continue;

                    string name = type.Name + "." + ((TestAttribute)attrs[0]).Name;
                    if (!string.IsNullOrEmpty(filter) &&
                        name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        object instance = m.IsStatic ? null : Activator.CreateInstance(type);
                        m.Invoke(instance, null);
                        passed++;
                        Console.WriteLine("  pass  " + name);
                    }
                    catch (TargetInvocationException tie)
                    {
                        Exception inner = tie.InnerException ?? tie;
                        failures.Add(name + ": " + inner.Message);
                        Console.WriteLine("  FAIL  " + name);
                        Console.WriteLine("        " + inner.Message);
                    }
                    catch (Exception ex)
                    {
                        failures.Add(name + ": " + ex.Message);
                        Console.WriteLine("  FAIL  " + name);
                        Console.WriteLine("        " + ex.Message);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failures.Count + " failed" +
                              (skipped > 0 ? ", " + skipped + " filtered out" : ""));
            if (failures.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Failures:");
                foreach (string f in failures) Console.WriteLine("  - " + f);
                return 1;
            }
            return 0;
        }
    }
}
