using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace nyxWM
{
    /// <summary>
    /// Enumerates the shell's AppsFolder, which is what the Start Menu uses for
    /// UWP / Microsoft Store applications. Desktop apps also appear there with a
    /// filesystem path; those are skipped because the shortcut scanner already
    /// covers them and provides real icons.
    /// </summary>
    internal static class UwpAppScanner
    {
        private const string AppsFolder = "shell:AppsFolder";

        internal static void Scan(List<RofiApp> apps, HashSet<string> seenNames)
        {
            object? shell = null;
            object? folder = null;
            object? items = null;

            try
            {
                shell = CreateShell();
                if (shell == null)
                    return;

                folder = shell.GetType().InvokeMember(
                    "NameSpace",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { AppsFolder });

                if (folder == null)
                    return;

                items = folder.GetType().InvokeMember(
                    "Items",
                    BindingFlags.InvokeMethod,
                    null,
                    folder,
                    null);

                if (items == null)
                    return;

                Type itemsType = items.GetType();
                int count = Convert.ToInt32(
                    itemsType.InvokeMember("Count", BindingFlags.GetProperty, null, items, null) ?? 0);

                for (int index = 0; index < count; index++)
                {
                    AddItem(itemsType, items, index, apps, seenNames);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UwpAppScanner: enumeration failed: {ex.Message}");
            }
            finally
            {
                ReleaseCom(items);
                ReleaseCom(folder);
                ReleaseCom(shell);
            }
        }

        private static void AddItem(
            Type itemsType,
            object items,
            int index,
            List<RofiApp> apps,
            HashSet<string> seenNames)
        {
            object? item = null;

            try
            {
                item = itemsType.InvokeMember(
                    "Item",
                    BindingFlags.InvokeMethod,
                    null,
                    items,
                    new object[] { index });

                if (item == null)
                    return;

                Type itemType = item.GetType();
                string name = ReadStringProperty(itemType, item, "Name");
                string appId = ReadStringProperty(itemType, item, "Path");

                if (name.Length == 0 || !IsUwpAppId(appId))
                    return;

                if (!seenNames.Add(name))
                    return;

                apps.Add(new RofiApp(
                    name,
                    "Windows Apps",
                    appId,
                    kind: RofiAppKind.Uwp));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UwpAppScanner: cannot read item {index}: {ex.Message}");
            }
            finally
            {
                ReleaseCom(item);
            }
        }

        private static bool IsUwpAppId(string appId)
        {
            // Desktop entries in AppsFolder expose a rooted executable path;
            // UWP entries expose an AppUserModelID such as
            // Microsoft.WindowsCalculator_8wekyb3d8bbwe!App.
            if (appId.Length == 0 || Path.IsPathRooted(appId))
                return false;

            return appId.Contains('!');
        }

        private static object? CreateShell()
        {
            try
            {
                Type? type = Type.GetTypeFromProgID("Shell.Application");
                return type == null ? null : Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UwpAppScanner: Shell.Application unavailable: {ex.Message}");
                return null;
            }
        }

        private static string ReadStringProperty(Type type, object instance, string property)
        {
            object? value = type.InvokeMember(
                property,
                BindingFlags.GetProperty,
                null,
                instance,
                null);

            return (value as string ?? string.Empty).Trim();
        }

        private static void ReleaseCom(object? instance)
        {
            if (instance != null && Marshal.IsComObject(instance))
            {
                try
                {
                    Marshal.ReleaseComObject(instance);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"UwpAppScanner: COM release failed: {ex.Message}");
                }
            }
        }
    }
}
