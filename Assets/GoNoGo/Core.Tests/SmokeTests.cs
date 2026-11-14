using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    public class SmokeTests
    {
        [Test]
        public void SystemRandom_WithSameSeed_IsDeterministic()
        {
            var a = new System.Random(42);
            var b = new System.Random(42);
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(a.Next(), b.Next());
        }
    }
}
