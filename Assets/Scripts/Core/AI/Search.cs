namespace Chess {
	using System.Collections.Generic;
	using System.Threading;
	using UnityEngine;
	using static System.Math;

	public class Search {

		const int transpositionTableSize = 64000;
		const int immediateMateScore = 100000;
		const int positiveInfinity = 9999999;
		const int negativeInfinity = -positiveInfinity;

		public event System.Action<Move> onSearchComplete;

		TranspositionTable tt;
		MoveGenerator moveGenerator;

		Move bestMoveThisIteration;
		int bestEvalThisIteration;
		Move bestMove;
		int bestEval;
		int currentIterativeSearchDepth;
		bool abortSearch;

		Move invalidMove;
		MoveOrdering moveOrdering;
		AISettings settings;
		Board board;
		Evaluation evaluation;

		// Diagnostics
		public SearchDiagnostics searchDiagnostics;
		int numNodes;
		int numQNodes;
		int numCutoffs;
		int numTranspositions;
		System.Diagnostics.Stopwatch searchStopwatch;

		public Search (Board board, AISettings settings) {
			this.board = board;
			this.settings = settings;
			evaluation = new Evaluation ();
			moveGenerator = new MoveGenerator ();
			tt = new TranspositionTable (board, transpositionTableSize);
			moveOrdering = new MoveOrdering (moveGenerator, tt);
			invalidMove = Move.InvalidMove;
			int s = TranspositionTable.Entry.GetSize ();
			//Debug.Log ("TT entry: " + s + " bytes. Total size: " + ((s * transpositionTableSize) / 1000f) + " mb.");
		}

		public void StartSearch () {
			InitDebugInfo ();

			// Initialize search settings
			bestEvalThisIteration = bestEval = 0;
			bestMoveThisIteration = bestMove = Move.InvalidMove;
			tt.enabled = settings.useTranspositionTable;

			// Clearing the transposition table before each search seems to help
			// This makes no sense to me, I presume there is a bug somewhere but haven't been able to track it down yet
			if (settings.clearTTEachMove) {
				tt.Clear ();
			}

			moveGenerator.promotionsToGenerate = settings.promotionsToSearch;
			currentIterativeSearchDepth = 0;
			abortSearch = false;
			searchDiagnostics = new SearchDiagnostics ();

			// Iterative deepening. This means doing a full search with a depth of 1, then with a depth of 2, and so on.
			// This allows the search to be aborted at any time, while still yielding a useful result from the last search.
			if (settings.useIterativeDeepening) {
				int targetDepth = (settings.useFixedDepthSearch) ? settings.depth : int.MaxValue;

				for (int searchDepth = 1; searchDepth <= targetDepth; searchDepth++) {
					SearchMoves (searchDepth, 0, negativeInfinity, positiveInfinity);
					if (abortSearch) {
						break;
					} else {
						currentIterativeSearchDepth = searchDepth;
						bestMove = bestMoveThisIteration;
						bestEval = bestEvalThisIteration;

						// Update diagnostics
						searchDiagnostics.lastCompletedDepth = searchDepth;
						searchDiagnostics.move = bestMove.Name;
						searchDiagnostics.eval = bestEval;
						searchDiagnostics.moveVal = Chess.PGNCreator.NotationFromMove (FenUtility.CurrentFen (board), bestMove);

						// Exit search if found a checkmate
						if (IsMateScore (bestEval) && !settings.endlessSearchMode) {
							break;
						}
					}
				}
			} else {
				SearchMoves (settings.depth, 0, negativeInfinity, positiveInfinity);
				bestMove = bestMoveThisIteration;
				bestEval = bestEvalThisIteration;
			}

			onSearchComplete?.Invoke (bestMove);

			if (!settings.useThreading) {
				LogDebugInfo ();
			}
		}

		public (Move move, int eval) GetSearchResult () {
			return (bestMove, bestEval);
		}

		public void EndSearch () {
			abortSearch = true;
		}

		int SearchMoves (int depth, int plyFromRoot, int alpha, int beta) {
			if (abortSearch) {
				return 0;
			}

			if (plyFromRoot > 0) {
				// The number of plies from the root of the search tree
				// Detect draw by repetition.
				// Returns a draw score even if this position has only appeared once in the game history (for simplicity).
				if (board.RepetitionPositionHistory.Contains (board.ZobristKey)) {
					return 0;
				}

				// Skip this position if a mating sequence has already been found earlier in
				// the search, which would be shorter than any mate we could find from here.
				// This is done by observing that alpha can't possibly be worse (and likewise
				// beta can't  possibly be better) than being mated in the current position.
				alpha = Max (alpha, -immediateMateScore + plyFromRoot); 		//At least the negative of the immediate mate score plus the current plyFromRoot
				beta = Min (beta, immediateMateScore - plyFromRoot); 			// At most the immediate mate score minus the current plyFromRoot
				if (alpha >= beta) { 											//The function returns alpha since it means the search has been cut off
					return alpha;
				}
			}

			// Try looking up the current position in the transposition table.
			// If the same position has already been searched to at least an equal depth
			// to the search we're doing now,we can just use the recorded evaluation.
			int ttVal = tt.LookupEvaluation (depth, plyFromRoot, alpha, beta);
			if (ttVal != TranspositionTable.lookupFailed) {
				numTranspositions++;
				if (plyFromRoot == 0) {
					bestMoveThisIteration = tt.GetStoredMove ();
					bestEvalThisIteration = tt.entries[tt.Index].value;
					//Debug.Log ("move retrieved " + bestMoveThisIteration.Name + " Node type: " + tt.entries[tt.Index].nodeType + " depth: " + tt.entries[tt.Index].depth);
				}
				return ttVal;
			}


			// Perform a limited search in quiet positions (positions where no captures or checks are possible)
			if (depth == 0) {
				int evaluation = QuiescenceSearch (alpha, beta);
				return evaluation;
			}

			// Generates all possible moves and orders them
			List<Move> moves = moveGenerator.GenerateMoves (board);
			moveOrdering.OrderMoves (board, moves, settings.useTranspositionTable);
			// Detect checkmate and stalemate when no legal moves are available
			if (moves.Count == 0) {
				if (moveGenerator.InCheck ()) {
					int mateScore = immediateMateScore - plyFromRoot;
					return -mateScore;
				} else {
					return 0;
				}
			}

			int evalType = TranspositionTable.UpperBound;
			Move bestMoveInThisPosition = invalidMove;

			for (int i = 0; i < moves.Count; i++) {
				board.MakeMove (moves[i], inSearch : true);
				// Recursively calls SearchMoves with reduced depth and updated bounds, and stores the negated evaluation in the eval variable
				int eval = -SearchMoves (depth - 1, plyFromRoot + 1, -beta, -alpha);
				board.UnmakeMove (moves[i], inSearch : true);
				numNodes++;

				// Move was *too* good, so opponent won't allow this position to be reached
				// Because a better move has already been found earlier
				// (by choosing a different move earlier on). Skip remaining moves.
				if (eval >= beta) {
					tt.StoreEvaluation (depth, plyFromRoot, beta, TranspositionTable.LowerBound, moves[i]);
					numCutoffs++;
					return beta;
				}

				// Found a new best move in this position
				if (eval > alpha) {
					evalType = TranspositionTable.Exact;
					bestMoveInThisPosition = moves[i];

					alpha = eval; //  alpha value is updated to the evaluation
					if (plyFromRoot == 0) {
						bestMoveThisIteration = moves[i];
						bestEvalThisIteration = eval;
					}
				}
			}

			tt.StoreEvaluation (depth, plyFromRoot, alpha, evalType, bestMoveInThisPosition);
			// After iterating through all the moves, the evaluation ( alpha), evalType, and bestMoveInThisPosition are stored 
			// in the transposition table using tt.StoreEvaluation. This allows future lookups to use the stored evaluation
			return alpha;

		}

		// Search capture moves until a 'quiet' position is reached.
		int QuiescenceSearch (int alpha, int beta) {
			// A player isn't forced to make a capture (typically), so see what the evaluation is without capturing anything.
			// This prevents situations where a player ony has bad captures available from being evaluated as bad,
			// when the player might have good non-capture moves available.
			int eval = evaluation.Evaluate (board);
			searchDiagnostics.numPositionsEvaluated++;
			if (eval >= beta) {
				return beta; 	// It means the current position is already good enough for the opponent. In this case, 
								//the function immediately returns beta, indicating that the opponent has a winning move
			}
			if (eval > alpha) {
				alpha = eval; 	// it means the current position is better than the previously known best move for the player. 
								//The alpha value is updated accordingly
			}

			var moves = moveGenerator.GenerateMoves (board, false); 	//false indicates that the function should not generate capture-only moves
			moveOrdering.OrderMoves (board, moves, false); 				//false indicates that the ordering should be performed in a non-capture context
			for (int i = 0; i < moves.Count; i++) {
				board.MakeMove (moves[i], true);
				eval = -QuiescenceSearch (-beta, -alpha);
				board.UnmakeMove (moves[i], true);
				numQNodes++; 		// Keep track of the number of quiescence search nodes explored

				if (eval >= beta) {
					numCutoffs++;
					return beta; 	// The opponent has a winning move in the current position. In this case, 
									//the function immediately returns beta, indicating that the opponent has a winning move
				}
				if (eval > alpha) {
					alpha = eval; 	// The current position is better than the previously known best move 
									//for the player. The alpha value is updated accordingly
				}
			}

			return alpha;
		}
		// Exploring quiet moves in the game tree until a stable position is reached (i.e., no more captures are available). 
		// The goal is to improve the accuracy of the evaluation by considering a set of quiet moves rather than just capturing moves

		public static bool IsMateScore (int score) {
			const int maxMateDepth = 1000;
			return System.Math.Abs (score) > immediateMateScore - maxMateDepth;
		}

		public static int NumPlyToMateFromScore (int score) {
			return immediateMateScore - System.Math.Abs (score);

		}

		void LogDebugInfo () {
			AnnounceMate ();
			Debug.Log ($"Best move: {bestMoveThisIteration.Name} Eval: {bestEvalThisIteration} Search time: {searchStopwatch.ElapsedMilliseconds} ms.");
			Debug.Log ($"Num nodes: {numNodes} num Qnodes: {numQNodes} num cutoffs: {numCutoffs} num TThits {numTranspositions}");
		}

		void AnnounceMate () {

			if (IsMateScore (bestEvalThisIteration)) {
				int numPlyToMate = NumPlyToMateFromScore (bestEvalThisIteration);
				//int numPlyToMateAfterThisMove = numPlyToMate - 1;

				int numMovesToMate = (int) Ceiling (numPlyToMate / 2f);

				string sideWithMate = (bestEvalThisIteration * ((board.WhiteToMove) ? 1 : -1) < 0) ? "Black" : "White";

				Debug.Log ($"{sideWithMate} can mate in {numMovesToMate} move{((numMovesToMate>1)?"s":"")}");
			}
		}

		void InitDebugInfo () {
			searchStopwatch = System.Diagnostics.Stopwatch.StartNew ();
			numNodes = 0;
			numQNodes = 0;
			numCutoffs = 0;
			numTranspositions = 0;
		}

		[System.Serializable]
		public class SearchDiagnostics {
			public int lastCompletedDepth;
			public string moveVal;
			public string move;
			public int eval;
			public bool isBook;
			public int numPositionsEvaluated;
		}

	}
}