using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SPSave.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = typeof(Program).Assembly.GetTypes()
                .Where(t => t.Name.EndsWith("Tests", StringComparison.Ordinal))
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(void))
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name)
                .ToList();

            var failed = 0;
            foreach (var test in tests)
            {
                try
                {
                    test.Invoke(null, null);
                    Console.WriteLine($"  ok    {test.DeclaringType.Name}.{test.Name}");
                }
                catch (TargetInvocationException e)
                {
                    failed++;
                    Console.WriteLine($"  FAIL  {test.DeclaringType.Name}.{test.Name}: {e.InnerException?.Message}");
                }
            }
            Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
            return failed == 0 ? 0 : 1;
        }
    }

    internal static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception($"{what}: expected {expected}, got {actual}");
            }
        }

        public static TException Throws<TException>(Action action, string what) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException e)
            {
                return e;
            }
            catch (Exception e)
            {
                throw new Exception($"{what}: expected {typeof(TException).Name}, got {e.GetType().Name}: {e.Message}");
            }
            throw new Exception($"{what}: expected {typeof(TException).Name}, nothing thrown");
        }
    }

    /// <summary>A fresh empty directory per test, removed afterwards.</summary>
    internal sealed class TempDir : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spsave-tests-" + Guid.NewGuid().ToString("N"));

        public TempDir()
        {
            Directory.CreateDirectory(Path);
        }

        public string File(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
