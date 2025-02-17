﻿using System;
using System.Collections;

namespace LibationWinForms
{
	internal class ObjectComparer<T> : IComparer where T : IComparable
	{
		public int Compare(object x, object y) => ((T)x).CompareTo(y);
	}
}
