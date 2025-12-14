// File: ClickThruBlockerWrapper.cs
// Target: .NET Framework 4.8 (KSP1)

using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

internal static class ClickThruBlockerProxy
{
    // Cache so we only pay reflection cost once.
    private static MethodInfo _guiLayoutWindowMI;
    private static bool _searched;

    /// <summary>
    /// Calls ClickThruBlocker.GUILayoutWindow(...) via reflection.
    /// Returns true if the call succeeded (and updates rect); false if CTB isn't present or signature mismatch.
    /// </summary>
    public static bool TryGUILayoutWindow(
        int id,
        ref Rect rect,
        GUI.WindowFunction func,
        string title,
        GUIStyle style,
        out Rect resultRect,
        params GUILayoutOption[] options)
    {
        resultRect = rect;

        var mi = GetGUILayoutWindowMethod();
        if (mi == null) return false;

        try
        {
            // Signature we want:
            // public static Rect GUILayoutWindow(int id, Rect screenRect, GUI.WindowFunction func,
            //                                  string text, GUIStyle style, params GUILayoutOption[] options)
            object[] args = new object[]
            {
                id,
                rect,
                func,
                title,
                style,
                options ?? Array.Empty<GUILayoutOption>()
            };

            var ret = mi.Invoke(null, args);
            if (ret is Rect r)
            {
                resultRect = r;
                rect = r; // keep caller rect updated
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Convenience: if CTB exists, use it; otherwise fall back to GUILayout.Window.
    /// </summary>
    public static Rect GUILayoutWindowOrFallback(
        int id,
        ref Rect rect,
        GUI.WindowFunction func,
        string title,
        GUIStyle style,
        params GUILayoutOption[] options)
    {
        if (TryGUILayoutWindow(id, ref rect, func, title, style, out var r, options))
            return r;

        // Fallback to stock GUILayout.Window
        Debug.Log("GUILayoutWindowFallback");
        var fallback = GUILayout.Window(id, rect, func, title, style, options ?? Array.Empty<GUILayoutOption>());
        rect = fallback;
        return fallback;
    }

    private static MethodInfo GetGUILayoutWindowMethod()
    {
        if (_searched) return _guiLayoutWindowMI;
        _searched = true;

        // Common type names seen in the wild; if yours differs, it’ll still be found by scanning assemblies below.
        var candidateTypeNames = new[]
        {
            "ClickThruBlocker.ClickThruBlocker", // common namespace+type
            "ClickThruBlocker",                  // sometimes type is root
            "ClickThroughBlocker.ClickThruBlocker",
            "ClickThroughBlocker.ClickThruBlocker.ClickThruBlocker"
        };

        Type t = null;

        // 1) Try direct Type.GetType (works if assembly-qualified or in mscorlib/current).
        foreach (var name in candidateTypeNames)
        {
            t = Type.GetType(name, throwOnError: false);
            if (t != null) break;
        }

        // 2) Scan all loaded assemblies (KSP-friendly approach).
        if (t == null)
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in asms)
            {
                foreach (var name in candidateTypeNames)
                {
                    t = asm.GetType(name, throwOnError: false);
                    if (t != null) break;
                }
                if (t != null) break;
            }
        }

        // 3) Last resort: find any type named "ClickThruBlocker" that has GUILayoutWindow.
        if (t == null)
        {
            t = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(x =>
                    string.Equals(x.Name, "ClickThruBlocker", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.FullName, "ClickThruBlocker", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.FullName, "ClickThruBlocker.ClickThruBlocker", StringComparison.OrdinalIgnoreCase));
        }

        if (t == null) return null;

        // Find best matching GUILayoutWindow overload.
        // We specifically want: (int, Rect, GUI.WindowFunction, string, GUIStyle, GUILayoutOption[])
        var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                       .Where(m => string.Equals(m.Name, "GUILayoutWindow", StringComparison.Ordinal))
                       .ToArray();

        foreach (var m in methods)
        {
            var p = m.GetParameters();
            if (p.Length != 6) continue;

            if (p[0].ParameterType != typeof(int)) continue;
            if (p[1].ParameterType != typeof(Rect)) continue;
            if (p[2].ParameterType != typeof(GUI.WindowFunction)) continue;
            if (p[3].ParameterType != typeof(string)) continue;
            if (p[4].ParameterType != typeof(GUIStyle)) continue;

            // last param should be GUILayoutOption[] (often marked with ParamArrayAttribute)
            if (p[5].ParameterType != typeof(GUILayoutOption[])) continue;

            if (m.ReturnType != typeof(Rect)) continue;

            _guiLayoutWindowMI = m;
            return _guiLayoutWindowMI;
        }

        return null;
    }
}
