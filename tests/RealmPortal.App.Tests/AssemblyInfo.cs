// The headless Avalonia tests share process-wide state: one UI dispatcher (Dispatcher.UIThread), the headless
// session that runs every [AvaloniaFact] on it, and app singletons such as Loc.Instance. With xUnit running test
// classes in parallel, Windows CI failed intermittently with PlatformNotSupportedException from Dispatcher.PushFrame
// and with assertions that saw another test's state (#217). Run the test classes one at a time; it costs no
// measurable time because the headless tests already queue on the single dispatcher.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
