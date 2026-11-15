"""Temporary start-up trace (never committed into the app): marks every statement of the MainWindow constructor and
OnLoaded with ms since process start and the JIT count, written to HOLM_DATA/trace.txt when the notch is ready.
Apply: python docs/opt/trace-patch.py   Revert: git checkout WinNotch/MainWindow.xaml.cs && rm WinNotch/OptTrace.cs"""
import re, pathlib

root = pathlib.Path(__file__).resolve().parents[2]
mw = root / 'WinNotch/MainWindow.xaml.cs'
src = mw.read_bytes().decode('utf-8-sig')
lines = src.split('\r\n')

def instrument(start_pat, label):
    i = next(n for n, l in enumerate(lines) if re.search(start_pat, l))
    assert lines[i + 1].strip() == '{'
    j, depth, out = i + 2, 1, []
    lines.insert(i + 2, f'        OptTrace.M("{label}:enter");')
    j = i + 3
    while True:
        l = lines[j]
        depth += l.count('{') - l.count('}')
        if depth == 0:
            break
        # top-level statement of the method (8 spaces), complete on this line
        if depth == 1 and re.match(r'^        [^ /}].*;\s*(//.*)?$', l) and not l.strip().startswith('return'):
            name = re.sub(r'[^A-Za-z0-9_.]', ' ', l.strip()).split()[0:3]
            lines.insert(j + 1, f'        OptTrace.M("{label}:{" ".join(name)}");')
            j += 1
        j += 1

instrument(r'^    public MainWindow\(\)', 'ctor')
instrument(r'^    void OnLoaded\(object sender, RoutedEventArgs e\)', 'loaded')
mw.write_text('﻿' + '\r\n'.join(lines), encoding='utf-8')

(root / 'WinNotch/OptTrace.cs').write_text('''using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
namespace WinNotch;
static class OptTrace
{
    static readonly DateTime Start = Process.GetCurrentProcess().StartTime;
    static readonly List<string> Lines = new();
    static double _last;
    static bool _armed;
    public static void M(string label)
    {
        double now = (DateTime.Now - Start).TotalMilliseconds;
        Lines.Add($"{now,7:F0} {now - _last,6:F0} {System.Runtime.JitInfo.GetCompiledMethodCount(),5} {label}");
        _last = now;
        if (_armed || Diag.DevFolder == null) return;
        _armed = true;
        Dispatcher.CurrentDispatcher.InvokeAsync(() => { M("ready"); File.WriteAllLines(Path.Combine(Diag.DevFolder, "trace.txt"), Lines); }, DispatcherPriority.ApplicationIdle);
    }
}
''', encoding='utf-8')
print('patched')
