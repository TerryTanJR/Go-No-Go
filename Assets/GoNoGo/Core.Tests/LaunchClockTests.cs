using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    public class LaunchClockTests
    {
        // 實測的 IM-2 倒數錄音：念「十」到「零」的開口時間（秒）
        static readonly float[] Calls = { 44.44f, 45.30f, 46.22f, 47.18f, 48.10f, 49.04f, 49.96f, 50.98f, 51.86f, 52.80f, 53.56f };

        [TestCase(-12.0, "T-00:00:12")]
        [TestCase(-10.0, "T-00:00:10")]
        [TestCase(-9.5, "T-00:00:10")]   // 念完「十」、還沒念「九」：畫面維持 10
        [TestCase(-9.0, "T-00:00:09")]
        [TestCase(-0.3, "T-00:00:01")]
        [TestCase(0.0, "T+00:00:00")]
        [TestCase(0.7, "T+00:00:00")]
        [TestCase(61.2, "T+00:01:01")]
        public void FormatChangesDigitWhenTheNumberIsSpoken(double t, string expected)
        {
            Assert.That(LaunchClock.Format(t), Is.EqualTo(expected));
        }

        [Test]
        public void EachSpokenNumberMapsToItsSecond()
        {
            for (int i = 0; i < Calls.Length; i++)
                Assert.That(LaunchClock.TFromAudio(Calls[i] + 0.1, Calls, 0.1), Is.EqualTo(-(10 - i)).Within(1e-5));
        }

        [Test]
        public void OutsideTheCountItRunsInRealTime()
        {
            Assert.That(LaunchClock.TFromAudio(Calls[0] - 2.0, Calls, 0.0), Is.EqualTo(-12.0).Within(1e-5));
            Assert.That(LaunchClock.TFromAudio(Calls[10] + 1.5, Calls, 0.0), Is.EqualTo(1.5).Within(1e-5));
        }

        [Test]
        public void AudioFromTIsTheInverse()
        {
            for (double t = -13; t <= 3; t += 0.37)
            {
                double a = LaunchClock.AudioFromT(t, Calls, 0.08);
                Assert.That(LaunchClock.TFromAudio(a, Calls, 0.08), Is.EqualTo(t).Within(1e-4), $"t={t}");
            }
        }
    }
}
