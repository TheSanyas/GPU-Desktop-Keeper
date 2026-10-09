using System;
using System.Runtime.InteropServices;

namespace GpuContextProbe {
    // Only this process owns these COM pointers. No process access or injection.
    public sealed class Device : IDisposable {
        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int D3D11CreateDevice(
            IntPtr adapter, uint driverType, IntPtr software, uint flags,
            IntPtr levels, uint levelCount, uint sdkVersion,
            out IntPtr device, out uint featureLevel, out IntPtr context);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct AdapterDesc {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId, DeviceId, SubSysId, Revision;
            public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
            public uint LuidLow;
            public int LuidHigh;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceDelegate(IntPtr instance, ref Guid iid, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetAdapterDelegate(IntPtr instance, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDescDelegate(IntPtr instance, out AdapterDesc desc);

        [StructLayout(LayoutKind.Sequential)]
        private struct BufferDesc {
            public uint ByteWidth, Usage, BindFlags, CpuAccessFlags, MiscFlags, StructureByteStride;
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateBufferDelegate(IntPtr instance, ref BufferDesc desc, IntPtr initial, out IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetRemovedReasonDelegate(IntPtr instance);

        private IntPtr device, context;
        private IntPtr helperBuffer1, helperBuffer2;
        public int HelperBuffersCreated { get; private set; }
        public string AdapterName { get; private set; }
        public uint VendorId { get; private set; }
        public uint FeatureLevel { get; private set; }

        private static T Method<T>(IntPtr instance, int index) where T : class {
            IntPtr vtable = Marshal.ReadIntPtr(instance);
            IntPtr address = Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        public Device(bool request11_1) {
            IntPtr dxgiDevice = IntPtr.Zero, adapter = IntPtr.Zero;
            IntPtr requestedLevels = IntPtr.Zero;
            try {
                uint level;
                if (request11_1) {
                    requestedLevels = Marshal.AllocHGlobal(4);
                    Marshal.WriteInt32(requestedLevels, 0xb100);
                }
                // HARDWARE=1, SDK_VERSION=7, default adapter, no debug layer.
                Marshal.ThrowExceptionForHR(D3D11CreateDevice(
                    IntPtr.Zero, 1, IntPtr.Zero, 0, requestedLevels, request11_1 ? 1u : 0u, 7,
                    out device, out level, out context));
                FeatureLevel = level;
                Guid iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
                Marshal.ThrowExceptionForHR(Method<QueryInterfaceDelegate>(device, 0)(device, ref iid, out dxgiDevice));
                // IDXGIDevice::GetAdapter slot 7; IDXGIAdapter::GetDesc slot 8.
                Marshal.ThrowExceptionForHR(Method<GetAdapterDelegate>(dxgiDevice, 7)(dxgiDevice, out adapter));
                AdapterDesc desc;
                Marshal.ThrowExceptionForHR(Method<GetDescDelegate>(adapter, 8)(adapter, out desc));
                AdapterName = desc.Description;
                VendorId = desc.VendorId;
                if (VendorId != 0x10de) {
                    throw new InvalidOperationException("Default D3D11 adapter is not NVIDIA. Test stopped: " + AdapterName);
                }
            } catch {
                Dispose();
                throw;
            } finally {
                if (requestedLevels != IntPtr.Zero) Marshal.FreeHGlobal(requestedLevels);
                if (adapter != IntPtr.Zero) Marshal.Release(adapter);
                if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
            }
        }

        public void InitializeHelperBuffers() {
            InitializeHelperBuffers(true, true);
        }

        public void InitializeHelperBuffers(bool small, bool large) {
            if (device == IntPtr.Zero) throw new ObjectDisposedException("Device");
            if (FeatureLevel != 0xb100) throw new InvalidOperationException("Helper initialization requires feature level 11.1");
            if (helperBuffer1 != IntPtr.Zero || helperBuffer2 != IntPtr.Zero) throw new InvalidOperationException("Already initialized");
            if (!small && !large) throw new ArgumentException("Select at least one buffer");
            // Observed persistent helper initialization: two dynamic CPU-writable vertex buffers.
            // No game hooks, rendering, mapping or command submission follows this initialization.
            var desc = new BufferDesc { ByteWidth = 96, Usage = 2, BindFlags = 1, CpuAccessFlags = 0x10000 };
            var create = Method<CreateBufferDelegate>(device, 3);
            if (small) {
                Marshal.ThrowExceptionForHR(create(device, ref desc, IntPtr.Zero, out helperBuffer1));
                HelperBuffersCreated++;
            }
            desc.ByteWidth = 8192;
            if (large) {
                Marshal.ThrowExceptionForHR(create(device, ref desc, IntPtr.Zero, out helperBuffer2));
                HelperBuffersCreated++;
            }
        }

        public int GetRemovedReason() {
            if (device == IntPtr.Zero) throw new ObjectDisposedException("Device");
            // ID3D11Device::GetDeviceRemovedReason, slot 39. Query only; no submission.
            return Method<GetRemovedReasonDelegate>(device, 39)(device);
        }

        public void Dispose() {
            if (helperBuffer2 != IntPtr.Zero) {
                IntPtr owned = helperBuffer2; helperBuffer2 = IntPtr.Zero; Marshal.Release(owned);
            }
            if (helperBuffer1 != IntPtr.Zero) {
                IntPtr owned = helperBuffer1; helperBuffer1 = IntPtr.Zero; Marshal.Release(owned);
            }
            if (context != IntPtr.Zero) {
                IntPtr owned = context; context = IntPtr.Zero; Marshal.Release(owned);
            }
            if (device != IntPtr.Zero) {
                IntPtr owned = device; device = IntPtr.Zero; Marshal.Release(owned);
            }
            GC.SuppressFinalize(this);
        }

        ~Device() { Dispose(); }
    }
}
