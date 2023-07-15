namespace Chess {
	public static class Piece {

		public const int None = 0;
		public const int King = 1;
		public const int Pawn = 2;
		public const int Knight = 3;
		public const int Bishop = 5;
		public const int Rook = 6;
		public const int Queen = 7;

		public const int White = 8;
		public const int Black = 16;

		const int typeMask = 0b00111;
		const int blackMask = 0b10000;
		const int whiteMask = 0b01000;
		const int colourMask = whiteMask | blackMask;
		// These lines define additional constants that are used as bit masks to extract information from the piece values. typeMask is used to isolate 
		//the piece type bits, blackMask is used to check if a piece is black, whiteMask is used to check if a piece is white, and colourMask is a 
		//combination of whiteMask and blackMask.

		public static bool IsColour (int piece, int colour) {
			return (piece & colourMask) == colour;
		}

		public static int Colour (int piece) {
			return piece & colourMask;
		}

		public static int PieceType (int piece) {
			return piece & typeMask;
		}

		public static bool IsRookOrQueen (int piece) {
			return (piece & 0b110) == 0b110;
		}

		public static bool IsBishopOrQueen (int piece) {
			return (piece & 0b101) == 0b101;
		}

		public static bool IsSlidingPiece (int piece) {
			return (piece & 0b100) != 0;
		}
	}
	// Provides a set of constants and utility methods to manipulate and extract information from chess piece values. 
	//It allows you to determine the type, color, and characteristics of a given chess piece by performing bitwise operations on the integer representations of the pieces.
}