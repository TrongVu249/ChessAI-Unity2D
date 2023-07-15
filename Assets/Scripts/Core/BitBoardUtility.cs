using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Chess {
	//Check bitboard representations of chess positions.
	public static class BitBoardUtility {
		public static bool ContainsSquare (ulong bitboard, int square) {
			return ((bitboard >> square) & 1) != 0;
		}
	}
}