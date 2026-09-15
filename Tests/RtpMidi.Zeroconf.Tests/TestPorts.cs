using System.Threading;

namespace jp.kshoji.rtpmidi.tests
{
    /// <summary>
    /// Allocates non-overlapping control ports (data = control + 1) for tests that call <see cref="RtpMidiServer.Start"/>.
    /// </summary>
    internal static class TestPorts
    {
        private static int next = 41000;

        public static int NextControlPort()
        {
            return Interlocked.Add(ref next, 2);
        }
    }
}
