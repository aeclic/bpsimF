using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Bpsim
{
	public struct Reference<T> where T : unmanaged
	{
		[NativeDisableUnsafePtrRestriction]
		private unsafe void* m_pointer;

		public unsafe readonly void* Pointer => m_pointer;

		public unsafe ref T Value => ref UnsafeUtility.AsRef<T>(m_pointer);

		public unsafe Reference(ref T value)
		{
			m_pointer = UnsafeUtility.AddressOf(ref value);
		}

		public unsafe Reference(void* pointer)
		{
			m_pointer = pointer;
		}

		public unsafe static Reference<T> Allocate(Allocator allocator)
		{
			int num = UnsafeUtility.SizeOf<T>();
			int alignment = UnsafeUtility.AlignOf<T>();
			void* intPtr = UnsafeUtility.Malloc(num, alignment, allocator);
			UnsafeUtility.MemClear(intPtr, num);
			return new Reference<T>(intPtr);
		}

		public unsafe static void Free(Reference<T> reference, Allocator allocator)
		{
			UnsafeUtility.Free(reference.m_pointer, allocator);
		}
	}
}
