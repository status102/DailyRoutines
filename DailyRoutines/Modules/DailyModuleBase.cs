using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Hooking;
using ECommons.Automation;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System;
using System.Reflection;
using DailyRoutines.Infos;
using Dalamud.Interface.Internal.Notifications;
using Dalamud.Plugin.Services;

namespace DailyRoutines.Modules;

public abstract class DailyModuleBase
{
    public bool Initialized { get; internal set; }
    public virtual string? Author { get; set; }
    protected TaskManager? TaskManager { get; set; }
    protected Overlay? Overlay { get; set; }

    protected string ModuleConfigFile =>
        Path.Join(Service.PluginInterface.GetPluginConfigDirectory(), $"{GetType().Name}.json");

    public virtual void Init() { }

    public virtual void ConfigUI() { }

    public virtual void OverlayUI() { }

    protected T LoadConfig<T>() where T : ModuleConfiguration
    {
        return LoadConfig<T>(GetType().Name);
    }

    protected T LoadConfig<T>(string key) where T : ModuleConfiguration
    {
        try
        {
            var configDirectory = Service.PluginInterface.GetPluginConfigDirectory();
            var configFile = Path.Combine(configDirectory, key + ".json");
            if (!File.Exists(configFile)) return default;
            var jsonString = File.ReadAllText(configFile);
            return JsonConvert.DeserializeObject<T>(jsonString);
        }
        catch (Exception ex)
        {
            Service.Log.Error($"Failed to load config for module: {key}");
            Service.Log.Error(ex.StackTrace);
            return default;
        }
    }

    private object LoadConfig(Type T, string key)
    {
        if (!T.IsSubclassOf(typeof(ModuleConfiguration)))
            throw new Exception($"{T} is not a ModuleConfiguration class.");
        try
        {
            var configDirectory = Service.PluginInterface.GetPluginConfigDirectory();
            var configFile = Path.Combine(configDirectory, key + ".json");
            if (!File.Exists(configFile)) return default;
            var jsonString = File.ReadAllText(configFile);
            return JsonConvert.DeserializeObject(jsonString, T);
        }
        catch (Exception ex)
        {
            Service.Log.Error($"Failed to load config for module: {key}");
            Service.Log.Error(ex.StackTrace);
            return default;
        }
    }

    protected void SaveConfig<T>(T config) where T : ModuleConfiguration
    {
        try
        {
            var configDirectory = Service.PluginInterface.GetPluginConfigDirectory();
            var configFile = Path.Combine(configDirectory, GetType().Name + ".json");
            var jsonString = JsonConvert.SerializeObject(config, Formatting.Indented);

            File.WriteAllText(configFile, jsonString);
        }
        catch (Exception ex)
        {
            Service.Log.Error($"Failed to load config for module: {GetType().Name}");
            Service.Log.Error(ex.StackTrace);
        }
    }

    private void SaveConfig(object config)
    {
        try
        {
            if (!config.GetType().IsSubclassOf(typeof(ModuleConfiguration)))
            {
                Service.Log.Error(
                    $"Failed to save Config: {config.GetType().Name} is not a subclass of ModuleConfiguration.");
                return;
            }

            var configDirectory = Service.PluginInterface.GetPluginConfigDirectory();
            var configFile = Path.Combine(configDirectory, GetType().Name + ".json");
            var jsonString = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(configFile, jsonString);
        }
        catch (Exception ex)
        {
            Service.Log.Error($"Failed to load config for module: {GetType().Name}");
            Service.Log.Error(ex.StackTrace);
        }
    }


    protected T GetConfig<T>(string key)
    {
        var moduleName = GetType().Name;

        try
        {
            if (!File.Exists(ModuleConfigFile))
            {
                Service.Log.Error($"Config file not found: {ModuleConfigFile}");
                return default;
            }

            var existingJson = File.ReadAllText(ModuleConfigFile);
            var existingConfig = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(existingJson);

            if (existingConfig == null)
            {
                Service.Log.Error("Failed to deserialize JSON to Dictionary.");
                return default;
            }

            if (existingConfig.TryGetValue(key, out var value))
            {
                try
                {
                    var configValue = value.ToObject<T>();
                    if (configValue == null) Service.Log.Error($"Failed to convert JToken to type {typeof(T).Name}");
                    return configValue;
                }
                catch (Exception ex)
                {
                    Service.Log.Error(ex, $"Exception while converting JToken to type {typeof(T).Name}");
                    return default;
                }
            }

            Service.Log.Error($"Key '{key}' not found in the config file.");
            return default;
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, $"Failed to get config for {moduleName}");
            return default;
        }
    }

    protected bool AddConfig(string key, object? config)
    {
        var moduleName = GetType().Name;

        try
        {
            Dictionary<string, object>? existingConfig;

            if (File.Exists(ModuleConfigFile))
            {
                var existingJson = File.ReadAllText(ModuleConfigFile);
                existingConfig = JsonConvert.DeserializeObject<Dictionary<string, object>>(existingJson);
                if (existingConfig != null && existingConfig.ContainsKey(key)) return false;
            }
            else
                existingConfig = [];

            existingConfig ??= [];
            existingConfig[key] = config;

            var jsonString = JsonConvert.SerializeObject(existingConfig, Formatting.Indented);

            File.WriteAllText(ModuleConfigFile, jsonString);

            return true;
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, $"Failed to write config for {moduleName}");
            return false;
        }
    }

    protected bool UpdateConfig(string key, object? newConfig)
    {
        var moduleName = GetType().Name;

        try
        {
            if (!File.Exists(ModuleConfigFile))
            {
                Service.Log.Error($"Config file for {moduleName} does not exist.");
                return false;
            }

            var existingJson = File.ReadAllText(ModuleConfigFile);
            var existingConfig = JsonConvert.DeserializeObject<Dictionary<string, object?>>(existingJson);

            if (!existingConfig.ContainsKey(key))
            {
                Service.Log.Error($"Key '{key}' does not exist in the config for {moduleName}.");
                return false;
            }

            existingConfig[key] = newConfig;

            var jsonString = JsonConvert.SerializeObject(existingConfig, Formatting.Indented);
            File.WriteAllText(ModuleConfigFile, jsonString);

            return true;
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, $"Failed to update config for {moduleName}");
            return false;
        }
    }

    protected bool RemoveConfig(string key)
    {
        var moduleName = GetType().Name;

        try
        {
            Dictionary<string, object>? existingConfig;

            if (File.Exists(ModuleConfigFile))
            {
                var existingJson = File.ReadAllText(ModuleConfigFile);
                existingConfig = JsonConvert.DeserializeObject<Dictionary<string, object>>(existingJson);
            }
            else
                return false;

            if (!existingConfig.Remove(key)) return false;

            var jsonString = JsonConvert.SerializeObject(existingConfig, Formatting.Indented);

            File.WriteAllText(ModuleConfigFile, jsonString);

            return true;
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, $"Failed to remove config for {moduleName}");
            return false;
        }
    }

    protected bool InterruptByConflictKey()
    {
        if (Service.KeyState[Service.Config.ConflictKey])
        {
            TaskManager?.Abort();
            Service.DalamudNotice.AddNotification(new()
            {
                Content = Service.Lang.GetText("ConflictKey-InterruptMessage"), Title = "Daily Routines",
                Type = NotificationType.Success
            });
            return true;
        }

        return false;
    }

    public virtual void Uninit()
    {
        Service.WindowManager.RemoveWindows(Overlay);
        Overlay = null;

        TaskManager?.Abort();
        TaskManager = null;

        var derivedInstance = GetType();
        // 字段
        foreach (var field in derivedInstance.GetFields(BindingFlags.Instance | BindingFlags.NonPublic |
                                                        BindingFlags.Public | BindingFlags.Static))
            if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Hook<>))
            {
                var hookInstance = field.GetValue(this);

                if (hookInstance != null)
                {
                    var disposeMethod = hookInstance.GetType().GetMethod("Dispose");
                    disposeMethod?.Invoke(hookInstance, null);

                    field.SetValue(this, null);
                }
            }

        // 函数
        foreach (var method in derivedInstance.GetMethods(BindingFlags.NonPublic | BindingFlags.Public |
                                                          BindingFlags.Instance))
            if (method.ReturnType == typeof(void) &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(IFramework))
                Service.FrameworkManager.Unregister(method);
    }
}
