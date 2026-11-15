# Holm Icons

The WPF app draws its icons from Segoe Fluent Icons, a font that ships with Windows and may not be redistributed.
On Linux and macOS the cross-platform app (`Holm.App`) uses `Holm.App/Assets/HolmIcons.ttf` instead: a small font
(under 20 KB) with only the icons Holm uses, taken from Microsoft's
[Fluent System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT) and filed under the *same code
points* Segoe uses. Code that shows an icon is therefore the same on every system (`""` is Play everywhere);
only `Holm.App/Fonts.cs` knows which font is behind it. On Windows the app keeps using Segoe, so nothing changes there.

## Adding an icon

1. Add a line to `icons.txt`: the Segoe code point the code uses, and the name of the Fluent System Icons glyph
   that should stand in for it (browse them at https://aka.ms/fluentui-system-icons; names are like `PlayRegular`
   or `StarFilled`).
2. Rebuild the font and commit both files:

       pip install fonttools
       npm pack @fluentui/react-icons
       tar xzf fluentui-react-icons-*.tgz package/lib/utils/fonts
       python3 build.py package/lib/utils/fonts

A code point that is missing from the font shows as an empty box on Linux and macOS, and only there: check new
icons on one of them.
