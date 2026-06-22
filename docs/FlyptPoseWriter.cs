using System;
using System.IO.MemoryMappedFiles;

namespace OxrmcFlypt
{
    /// <summary>
    /// Minimal, dependency-free reference writer for the OpenXR-MotionCompensation
    /// (OXRMC) "flypt" shared-memory tracker. Write your rig's pose to the
    /// "motionRigPose" memory-mapped file and OXRMC reads it when its tracker
    /// type = flypt, subtracting the motion so the VR view stays world-locked.
    ///
    /// Payload = 6 contiguous little-endian float64 (doubles), from offset 0:
    ///   [0] sway   (metres)
    ///   [1] surge  (metres)
    ///   [2] heave  (metres)
    ///   [3] yaw    (radians)
    ///   [4] roll   (radians)
    ///   [5] pitch  (radians)
    ///
    /// Write your platform's COMMANDED pose - after washout / limiting / per-axis
    /// allocation, i.e. what the rig physically does, NOT the raw game telemetry -
    /// as deltas from the neutral rest pose, at your native update rate. Set any
    /// axis you do not drive to 0. Final axis polarity is tuned on the rig via
    /// OXRMC's own calibration (CTRL+DEL) and per-axis invert, so if a driven axis
    /// compensates the wrong way, flip its sign here or in OXRMC.
    ///
    /// This is the same interface the open-source OXRMC Bridge SimHub plugin uses;
    /// it is reproduced here standalone so it can be dropped straight into another
    /// motion application (e.g. alongside an existing MemoryMappedFile helper).
    /// .NET Framework 4.x / .NET 5+; x86/x64 little-endian.
    /// </summary>
    public sealed class FlyptPoseWriter : IDisposable
    {
        /// <summary>Name OXRMC's flypt tracker reads. Both processes must run in the same Windows session (the interactive user).</summary>
        public const string MmfName = "motionRigPose";

        private const int Capacity = 256;     // OXRMC maps 256 bytes; only the first 48 are used
        private const int FieldCount = 6;

        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _view;
        private readonly double[] _pose = new double[FieldCount];

        /// <summary>Create-or-open the shared file. Safe whether OXRMC or you start first.</summary>
        public void Open()
        {
            _mmf = MemoryMappedFile.CreateOrOpen(MmfName, Capacity);
            _view = _mmf.CreateViewAccessor(0, FieldCount * sizeof(double));
        }

        /// <summary>Write the full 6-DOF pose. Call once per control tick at your native rate.</summary>
        public void Write(double swayMeters, double surgeMeters, double heaveMeters,
                          double yawRad, double rollRad, double pitchRad)
        {
            _pose[0] = swayMeters;
            _pose[1] = surgeMeters;
            _pose[2] = heaveMeters;
            _pose[3] = yawRad;
            _pose[4] = rollRad;
            _pose[5] = pitchRad;
            for (int i = 0; i < FieldCount; i++)
                _view.Write(i * sizeof(double), _pose[i]);
        }

        public void Dispose()
        {
            if (_view != null) { try { _view.Dispose(); } catch { } _view = null; }
            if (_mmf != null) { try { _mmf.Dispose(); } catch { } _mmf = null; }
        }
    }
}
