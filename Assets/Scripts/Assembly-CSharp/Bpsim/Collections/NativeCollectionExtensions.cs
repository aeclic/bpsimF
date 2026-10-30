using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Bpsim.Collections
{
	public static class NativeCollectionExtensions
	{
		public static NativeParallelHashMap<TKey, TValue> ToNative<TKey, TValue>(this Dictionary<TKey, TValue> source, Allocator allocator) where TKey : unmanaged, IEquatable<TKey> where TValue : unmanaged
		{
			NativeParallelHashMap<TKey, TValue> result = new NativeParallelHashMap<TKey, TValue>(source.Count, allocator);
			foreach (KeyValuePair<TKey, TValue> item in source)
			{
				result.Add(item.Key, item.Value);
			}
			return result;
		}
	}
}
