// using System;
// using System.Drawing.Text;
// using System.IO;
// using Avalonia;
// using Avalonia.Media;
// using Avalonia.Platform;
// using ShimSkiaSharp;
//
// namespace UiharuMind.Utils;
//
// public static class FontUtils
// {
//     public static string GetFontFamilyName(string fontFilePath)
//     {
// // #pragma warning disable CA1416
// //         using var fontCollection = new PrivateFontCollection();
// //         fontCollection.AddFontFile(fontFilePath);
// //         if (fontCollection.Families.Length > 0)
// //         {
// //             return fontCollection.Families[0].Name;
// //         }
// //         else
// //         {
// //             throw new FileNotFoundException("Font file not found or invalid.", fontFilePath);
// //         }
// //
// // #pragma warning restore CA1416
//         GlyphTypeface glyphTypeface = new GlyphTypeface(new Uri("file:///C:\\WINDOWS\\Fonts\\Kooten.ttf"));
//     }
// }