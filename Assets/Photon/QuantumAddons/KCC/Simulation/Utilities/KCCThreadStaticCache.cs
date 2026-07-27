namespace Quantum
{
	using System;
	using System.Collections.Generic;
	using System.Runtime.CompilerServices;

	public static class KCCThreadStaticCache
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static T Get<T>() where T : new()
		{
			return KCCThreadStaticCache<T>.Get();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Return<T>(T item) where T : new()
		{
			KCCThreadStaticCache<T>.Return(item);
		}
	}

	public static class KCCThreadStaticCache<T> where T : new()
	{
		[ThreadStatic]
		private static Stack<T> _items;

		public static T Get()
		{
			if (_items == null)
			{
				_items = new Stack<T>();
			}

			return _items.Count > 0 ? _items.Pop() : new T();
		}

		public static void Return(T item)
		{
			_items.Push(item);
		}
	}
}
