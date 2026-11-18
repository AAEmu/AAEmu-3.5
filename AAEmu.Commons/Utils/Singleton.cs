using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Commons.Utils;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA1000 // Do not declare static members on generic types
#pragma warning disable CA1508 // Double-checked locking: analyzer cannot model cross-thread writes between outer null-check and lock acquire

/// <summary>
/// Base class used for singletons
/// </summary>
/// <typeparam name="T">The class type</typeparam>
public abstract class Singleton<T> where T : class
{
    private static T s_instance;

    /// <summary>
    /// Gets the instance of the singleton. Resolves from the DI container when available,
    /// caching the result so subsequent calls are free. Falls back to reflection when DI
    /// is not configured (e.g. unit tests).
    /// </summary>
    public static T Instance
    {
        get
        {
            if (s_instance != null)
                return s_instance;

            if (SingletonContainer.ServiceProvider?.GetService<T>() is { } fromDi)
            {
                lock (typeof(T))
                {
                    s_instance ??= fromDi;
                }
                return s_instance;
            }

            OnInit();
            return s_instance;
        }
    }

    private static void OnInit()
    {
        if (s_instance != null)
            return;
        lock (typeof(T))
        {
            if (s_instance != null)
                return;
            if (typeof(T).GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null) == null)
                throw new InvalidOperationException(
                    $"{typeof(T).Name} has no parameterless constructor. " +
                    "Resolve it from DI, or instantiate it with explicit dependencies.");
            s_instance = typeof(T).InvokeMember(typeof(T).Name,
                BindingFlags.CreateInstance |
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic,
                null, null, null) as T;
        }
    }

    /// <summary>
    /// The instance when it can be had — from the DI container, or from a parameterless constructor — and false when
    /// neither is possible (a unit test without a host, or a caller running before the container is built). Unlike
    /// <see cref="Instance"/>, never throws: for a caller that has a sensible answer without the manager.
    /// </summary>
    public static bool TryGetInstance(out T instance)
    {
        instance = s_instance;
        if (instance != null)
            return true;

        if (SingletonContainer.ServiceProvider?.GetService<T>() is { } fromDi)
        {
            lock (typeof(T))
            {
                s_instance ??= fromDi;
            }

            instance = s_instance;
            return true;
        }

        if (typeof(T).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null) == null)
            return false;

        OnInit();
        instance = s_instance;
        return instance != null;
    }
}
