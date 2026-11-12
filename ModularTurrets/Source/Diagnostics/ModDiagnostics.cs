using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

// Called from failure paths only. Keep messages independent of localization,
// since a broken translation can itself be the reason for the exception.
public static class ModDiagnostics
{
    private const int Limit = 32;
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    public static void WarningOnce(string context, Exception error) => Report(context, error, false);
    public static void ErrorOnce(string context, Exception error) => Report(context, error, true);

    private static void Report(string context, Exception error, bool isError)
    {
        if (error == null) return;
        try
        {
            var cause = error.GetBaseException();
            var site = cause.TargetSite;
            // Messages and object IDs can vary every frame for the same fault.
            string key = context + "|" + cause.GetType().FullName + "|"
                + site?.DeclaringType?.FullName + "." + site?.Name;
            bool last;
            lock (Reported)
            {
                if (Reported.Count >= Limit || !Reported.Add(key)) return;
                last = Reported.Count == Limit;
            }

            string message = "[Modular Turrets " + typeof(ModDiagnostics).Assembly.GetName().Version
                + "] Exception in " + context + ". Repeats at this failure site are suppressed for this session.\n" + error;
            if (last) message += "\nSession diagnostic limit reached; further caught-exception reports are suppressed.";
            if (isError) Log.Error(message); else Log.Warning(message);
        }
        catch { /* Diagnostics must never replace the original failure handling. */ }
    }
}
