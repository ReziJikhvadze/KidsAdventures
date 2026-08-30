using System.Collections.Concurrent;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Adventrya.Story.Tests;

/// <summary>
/// The flat pictures the offline suites are drawn against, encoded once each.
///
/// Half a dozen suites each had their own copy of "make a solid PNG of this size", and between them
/// they called it thousands of times a run — a single book render asks for fourteen, and a pipeline
/// test that walks eight spreads asks for dozens. The bytes are a pure function of the size and the
/// colour, so all that repetition bought was the same PNG encoder pass over and over: it was the
/// largest single cost in the suite after the pipeline itself.
///
/// So each distinct picture is built once and handed out as a fresh copy. The copy matters: callers
/// pass these arrays into services that are free to do anything with them, and they still receive a
/// private array exactly as they did when every call encoded afresh. The cache is keyed on
/// everything that decides the bytes, so no caller can be handed a picture it did not ask for.
/// </summary>
internal static class SyntheticImages
{
    private static readonly ConcurrentDictionary<(int Width, int Height, uint Colour), byte[]> Cache = new();

    /// <summary>A solid rectangle of <paramref name="colour"/>, opaque.</summary>
    public static byte[] SolidPng(int width, int height, Rgba32 colour) =>
        Cache.GetOrAdd((width, height, colour.PackedValue), key =>
        {
            using var image = new Image<Rgba32>(
                key.Width, key.Height, new Rgba32 { PackedValue = key.Colour });
            using var buffer = new MemoryStream();
            image.Save(buffer, new PngEncoder());
            return buffer.ToArray();
        }).ToArray();

    /// <summary>
    /// The shape most suites want: a rectangle distinguishable from its neighbours by one channel,
    /// so that "these are two different pictures" is not a vacuous assertion about two blank ones.
    /// </summary>
    public static byte[] SolidPng(int width, int height, byte red = 0) =>
        SolidPng(width, height, new Rgba32(red, 0, 0, 255));

    /// <summary>A solid rectangle named by its three channels.</summary>
    public static byte[] SolidPng(int width, int height, (byte R, byte G, byte B) colour) =>
        SolidPng(width, height, new Rgba32(colour.R, colour.G, colour.B, 255));
}
