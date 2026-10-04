using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Fantasy.Helper;
using NUnit.Framework;
namespace Fantasy.Net.Tests;

[NonParallelizable]
public sealed class SocketBufferTests
{
    private static void Require(bool condition, string message) => Assert.That(condition, Is.True, message);
    private static void SetBuffer(Func<int> read, Action<int> write, int step, int attempts)
    {
        MethodInfo? method = typeof(NetworkHelper).GetMethod("SetBufferWithinBudget", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Socket initialization must use the bounded option-setting algorithm.");
        try { method!.Invoke(null, new object[] { read, write, step, attempts }); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
    }
    [Test]
    public void same_default_target_one_write()
    {
        int current = 65536, writes = 0;
        SetBuffer(() => current, v => { current = v; writes++; }, 1024, 100000);
        Require(current == 102465536 && writes == 1, "target or budget incorrect");

    }
    [Test]
    public void os_clamp()
    {
        int current = 65536, writes = 0;
        SetBuffer(() => current, v => { current = Math.Min(v, 262144); writes++; }, 1024, 100000);
        Require(current == 262144 && writes == 1, "clamp repeated");

    }
    [Test]
    public void os_scaling()
    {
        int current = 65536, writes = 0;
        SetBuffer(() => current, v => { current = v * 2; writes++; }, 1024, 100000);
        Require(current == 204931072 && writes == 1, "scaling repeated");

    }
    [Test]
    public void rejection_fallback()
    {
        int current = 65536, writes = 0;
        SetBuffer(() => current, v => { writes++; if (v > 65536 + 777 * 1024) throw new SocketException(); current = v; }, 1024, 100000);
        Require(current == 65536 + 777 * 1024 && writes <= 32, "fallback incorrect");

    }
    [Test]
    public void all_rejected()
    {
        int writes = 0;
        SetBuffer(() => 65536, v => { writes++; throw new SocketException(); }, 1, int.MaxValue);
        Require(writes <= 32, "budget exceeded");

    }
    [Test]
    public void overflow()
    {
        int current = int.MaxValue - 3, writes = 0;
        SetBuffer(() => current, v => { Require(v > 0, "overflow"); current = v; writes++; }, 2, int.MaxValue);
        Require(current == int.MaxValue - 1 && writes == 1, "overflow target incorrect");

    }
    [Test]
    public void nonpositive_attempts()
    {
        foreach (int n in new[] { 0, -1 }) SetBuffer(() => throw new Exception("read"), v => throw new Exception("write"), 0, n);

    }
    [Test]
    public void nonpositive_step_rejected()
    {
        foreach (int step in new[] { -1 })
        {
            bool caught = false; try { SetBuffer(() => 65536, v => { }, step, 1); } catch (ArgumentOutOfRangeException) { caught = true; }
            Require(caught, "invalid step accepted");
        }

    }
    [Test]
    public void disposed_errors_propagate()
    {
        bool caught = false; try { SetBuffer(() => throw new ObjectDisposedException("socket"), v => { }, 1024, 1); } catch (ObjectDisposedException) { caught = true; }
        Require(caught, "disposed hidden");

    }
    [Test]
    public void seeded_monotonic_limit_model_10000()
    {
        var random = new Random(20261004);
        for (int i = 0; i < 10000; i++)
        {
            int initial = random.Next(1, 1000000), step = random.Next(1, 1000000), attempts = random.Next(1, int.MaxValue);
            long ceiling = Math.Min((long)attempts, ((long)int.MaxValue - initial) / step), allowed = random.NextInt64(ceiling + 1);
            int current = initial, writes = 0;
            SetBuffer(() => current, v => { writes++; Require(v >= initial, "negative growth"); if ((long)v > initial + allowed * step) throw new SocketException(); current = v; }, step, attempts);
            Require(current == initial + allowed * step && writes <= 32, "model mismatch");
        }

    }
    [Test]
    public void maximum_depth_success_paths()
    {
        foreach (int allowed in new[] { 1, 1073741823, int.MaxValue - 1 })
        {
            int current = 0, writes = 0;
            SetBuffer(() => current, v => { writes++; if (v > allowed) throw new SocketException(); current = v; }, 1, int.MaxValue);
            Require(current == allowed && writes <= 32, "maximum depth mismatch");
        }

    }
    [Test]
    public void ZeroStepDoesNotAccessOptions() => SetBuffer(() => throw new Exception("read"), _ => throw new Exception("write"), 0, 100000);
    [Test]
    public void NoRepresentableGrowthDoesNotWrite() => SetBuffer(() => int.MaxValue, _ => throw new Exception("write"), 1024, 100000);
    [TestCase(true)]
    [TestCase(false)]
    public void PublicHelpersRejectNegativeGrowth(bool receive)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            if (receive) socket.SetReceiveBufferToOSLimit(-1, 1);
            else socket.SetSendBufferToOSLimit(-1, 1);
        });
    }
    [TestCase(SocketType.Stream)]
    [TestCase(SocketType.Dgram)]
    public void RealSocketOptionsPreserveWindowsTarget(SocketType type)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, type, type == SocketType.Stream ? ProtocolType.Tcp : ProtocolType.Udp);
        int receive = socket.ReceiveBufferSize, send = socket.SendBufferSize;
        socket.SetSocketBufferToOsLimit();
        if (OperatingSystem.IsWindows())
        {
            Assert.That(socket.ReceiveBufferSize, Is.EqualTo(receive + 102400000));
            Assert.That(socket.SendBufferSize, Is.EqualTo(send + 102400000));
        }
        else
        {
            Assert.That(socket.ReceiveBufferSize, Is.GreaterThan(0));
            Assert.That(socket.SendBufferSize, Is.GreaterThan(0));
        }
    }
    [Test]
    public void NetAndUnityHelperSourcesRemainIdentical()
    {
        DirectoryInfo? root = new(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Fantasy.Packages"))) root = root.Parent;
        Assert.That(root, Is.Not.Null);
        string folder = root!.FullName;
        string Read(string package) => File.ReadAllText(Path.Combine(folder, "Fantasy.Packages", package, "Runtime/Core/Helper/NetworkHelper.cs")).Replace("\r\n", "\n");
        Assert.That(Read("Fantasy.Unity"), Is.EqualTo(Read("Fantasy.Net")));
    }
}
