using System;
namespace Chess {
	public struct Coord : IComparable<Coord> {
		public readonly int fileIndex;
		public readonly int rankIndex;

		public Coord (int fileIndex, int rankIndex) {
			this.fileIndex = fileIndex;
			this.rankIndex = rankIndex;
		}

		public bool IsLightSquare () {
			return (fileIndex + rankIndex) % 2 != 0;
		}

		public int CompareTo (Coord other) {
			return (fileIndex == other.fileIndex && rankIndex == other.rankIndex) ? 0 : 1;
		}
	}
	//provides a simple representation of a coordinate on a chessboard and includes methods 
	//for determining if the coordinate represents a light square and comparing coordinates 
	//to each other.
}