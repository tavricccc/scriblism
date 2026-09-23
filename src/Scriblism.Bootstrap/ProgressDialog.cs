using System.Runtime.InteropServices;

namespace Scriblism.Bootstrap;

/// <summary>
/// The shell's own progress dialog, used while the Windows App SDK runtime downloads.
/// </summary>
/// <remarks>
/// The bootstrap launcher is plain .NET with no UI framework — it cannot have one, because it
/// runs before the framework it is fetching exists. Writing a window class and a message pump
/// by hand to draw one progress bar would be a hundred lines of interop that can go wrong on
/// its own; the shell has shipped this dialog since Windows 2000 and it costs a COM interface
/// declaration.
///
/// Without it, the first install is tens of megabytes of complete silence after a dialog that
/// said "yes, download it" — indistinguishable from an installer that has hung.
/// </remarks>
internal sealed class ProgressDialog : IDisposable
{
    private const uint Modal = 0x00000001;
    private const uint AutoTime = 0x00000002;
    private const uint NoMinimize = 0x00000008;

    private readonly IProgressDialog _dialog;
    private bool _started;

    public ProgressDialog(string title, string line)
    {
        _dialog = (IProgressDialog)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("F8383852-FCD3-11d1-A6B9-006097DF5BD4"))!)!;

        _dialog.SetTitle(title);
        _dialog.SetLine(1, line, false, IntPtr.Zero);
        // Modal to nothing in particular: the launcher has no window of its own, and the flag
        // is what keeps the dialog in front of whatever the person was doing instead.
        _dialog.StartProgressDialog(IntPtr.Zero, null, Modal | AutoTime | NoMinimize, IntPtr.Zero);
        _started = true;
    }

    /// <summary>True once the person has pressed Cancel; the caller is expected to stop.</summary>
    public bool Cancelled => _started && _dialog.HasUserCancelled();

    /// <summary>Replaces the description without touching the bar.</summary>
    public void Line(string text)
    {
        if (_started) _dialog.SetLine(2, text, false, IntPtr.Zero);
    }

    public void Report(long completed, long? total)
    {
        if (!_started) return;

        // A total of zero leaves the bar in its indeterminate marquee, which is the honest
        // display for a server that would not say how large the file is.
        _dialog.SetProgress64((ulong)completed, (ulong)(total ?? 0));
        _dialog.SetLine(2, Describe(completed, total), false, IntPtr.Zero);
    }

    private static string Describe(long completed, long? total)
    {
        const double Megabyte = 1024 * 1024;
        return total is > 0
            ? $"{completed / Megabyte:0.0} MB / {total.Value / Megabyte:0.0} MB"
            : $"{completed / Megabyte:0.0} MB";
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;

        try
        {
            _dialog.StopProgressDialog();
        }
        catch (COMException)
        {
            // The dialog was already torn down; nothing here is worth failing an install over.
        }

        Marshal.FinalReleaseComObject(_dialog);
    }

    [ComImport]
    [Guid("EBBC7C04-315E-11d2-B62F-006097DF5BD4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IProgressDialog
    {
        void StartProgressDialog(IntPtr parent, [MarshalAs(UnmanagedType.IUnknown)] object? reserved, uint flags, IntPtr future);

        void StopProgressDialog();

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetAnimation(IntPtr instance, ushort animation);

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        bool HasUserCancelled();

        void SetProgress(uint completed, uint total);

        void SetProgress64(ulong completed, ulong total);

        void SetLine(uint line, [MarshalAs(UnmanagedType.LPWStr)] string text,
            [MarshalAs(UnmanagedType.Bool)] bool compactPath, IntPtr future);

        void SetCancelMsg([MarshalAs(UnmanagedType.LPWStr)] string text, IntPtr future);

        void Timer(uint action, IntPtr future);
    }
}
