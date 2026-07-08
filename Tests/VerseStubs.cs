// Verse 类型桩，用于纯逻辑测试，无 RimWorld 运行时依赖
using System;
using System.Collections.Generic;

namespace Verse
{
    /// <summary>
    /// Pawn 桩：仅提供类型标识，无游戏逻辑
    /// </summary>
    public class Pawn
    {
        public int thingIDNumber;
    }

    /// <summary>
    /// Log 桩：捕获日志消息到静态列表，供测试断言
    /// </summary>
    public static class Log
    {
        public static readonly System.Collections.Generic.List<string> Messages = new();
        public static void Message(string msg) { Messages.Add(msg); System.Diagnostics.Debug.WriteLine(msg); }
        public static void Warning(string msg) { Messages.Add(msg); System.Diagnostics.Debug.WriteLine(msg); }
        public static void Error(string msg) { Messages.Add(msg); System.Diagnostics.Debug.WriteLine(msg); }
    }

    /// <summary>
    /// ModSettings 桩：提供 ExposeData 虚方法和 Write 空实现
    /// </summary>
    public class ModSettings
    {
        public virtual void ExposeData() { }
        public void Write() { }
    }

    /// <summary>
    /// Mod 桩：提供 GetSettings<T> 空实现
    /// </summary>
    public class Mod
    {
        public Mod(ModContentPack content) { }
        protected T GetSettings<T>() where T : ModSettings, new() => new T();
    }

    /// <summary>
    /// ModContentPack 桩
    /// </summary>
    public class ModContentPack { }

    /// <summary>
    /// Scribe_Collections 桩：Look 方法为空操作
    /// </summary>
    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T>? list, string label, LookMode lookMode = LookMode.Value) { }
    }

    /// <summary>
    /// Scribe_Values 桩：Look 方法为空操作
    /// </summary>
    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default!) { }
    }

    /// <summary>
    /// LookMode 枚举桩
    /// </summary>
    public enum LookMode
    {
        Value,
        Reference,
        Deep
    }

    /// <summary>
    /// Rect 结构体桩
    /// </summary>
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        {
            this.x = x; this.y = y; this.width = width; this.height = height;
        }
    }

    /// <summary>
    /// Listing_Standard 桩
    /// </summary>
    public class Listing_Standard
    {
        public void Begin(Rect rect) { }
        public void Label(string label) { }
        public void End() { }
    }

    /// <summary>
    /// StaticConstructorOnStartup 属性桩
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class StaticConstructorOnStartupAttribute : Attribute { }

    /// <summary>
    /// TaggedString 桩：支持与 string 的隐式转换
    /// </summary>
    public struct TaggedString
    {
        private readonly string _value;
        public TaggedString(string value) => _value = value ?? "";
        public static implicit operator string(TaggedString tagged) => tagged._value;
        public static implicit operator TaggedString(string value) => new(value);
        public override string ToString() => _value ?? "";
    }

    /// <summary>
    /// Translate 扩展方法桩：直接返回键名
    /// </summary>
    public static class VerseStringExtensions
    {
        public static TaggedString Translate(this string key) => new TaggedString(key);
    }
}
