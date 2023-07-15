namespace Chess.Game {
	using System.Threading.Tasks;
	using System.Threading;

	public class AIPlayer : Player {
	//Kế thừa từ class Player

		const int bookMoveDelayMillis = 250;

		Search search;
		AISettings settings;
		bool moveFound;
		Move move;
		Board board;
		CancellationTokenSource cancelSearchTimer;

		Book book;

		public AIPlayer (Board board, AISettings settings) {
			this.settings = settings;
			this.board = board;
			settings.requestAbortSearch += TimeOutThreadedSearch;
			settings.useFixedDepthSearch = false;
			search = new Search (board, settings);
			search.onSearchComplete += OnSearchComplete;
			search.searchDiagnostics = new Search.SearchDiagnostics ();
			book = BookCreator.LoadBookFromFile (settings.book);
		}
		//setup AI, gọi đến các chức năng của Search, với 2 tham số board và setting

		// Cập nhật running trên luồng chính của Unity. Điều này được sử dụng để trả lại 
		//các move đã chọn để không kết thúc ở một luồng khác và không thể giao tiếp với Unity.
		public override void Update () {
			if (moveFound) {
				moveFound = false;
				ChoseMove (move);
			}

			settings.diagnostics = search.searchDiagnostics;

		}

		public override void NotifyTurnToMove () {

			search.searchDiagnostics.isBook = false;
			moveFound = false;

			Move bookMove = Move.InvalidMove;
			if (settings.useBook && board.plyCount <= settings.maxBookPly) {
				if (book.HasPosition (board.ZobristKey)) {
					bookMove = book.GetRandomBookMoveWeighted (board.ZobristKey);
				}
			}
			//Check xem số nước đi hiện tại của bàn cờ có bé hơn book không, nếu có 
			//thì bookMove sẽ được set random weighted move từ book
			if (bookMove.IsInvalid) {
				if (settings.useThreading) {
					StartThreadedSearch ();
				} else {
					StartSearch ();
				}
			//nếu bookmove k khả dụng, tức là vị trí hiện tại chưa đc lưu trữ trong book,
			//tiến hành tìm kiếm nước đi tiếp theo sử dụng một thread riêng (nếu có), nếu 
			//không có thread khả dụng thì tiến hành single-threaded search.
			} else {
			
				search.searchDiagnostics.isBook = true;
				search.searchDiagnostics.moveVal = Chess.PGNCreator.NotationFromMove (FenUtility.CurrentFen(board), bookMove);
				settings.diagnostics = search.searchDiagnostics;
				Task.Delay (bookMoveDelayMillis).ContinueWith ((t) => PlayBookMove (bookMove));
				
			}
			//Nếu bookmove khả dụng, có tồn tại thì set moveVal tới một giá trị tương ứng của 
			//move và bàn cờ. Diagnostics của setting sẽ được gán giá trị từ diagnostics của search.
			//Trì hoãn việc thực thi bước đi tiếp theo trước khi chơi move từ cuốn sách.
		}

		void StartSearch () {
			search.StartSearch ();
			moveFound = true;
		}
		//Gọi đến chức năng tìm kiếm nước đi ở Search
		void StartThreadedSearch () {
			//Thread thread = new Thread (new ThreadStart (search.StartSearch));
			//thread.Start ();
			Task.Factory.StartNew (() => search.StartSearch (), TaskCreationOptions.LongRunning);

			if (!settings.endlessSearchMode) {
				cancelSearchTimer = new CancellationTokenSource ();
				Task.Delay (settings.searchTimeMillis, cancelSearchTimer.Token).ContinueWith ((t) => TimeOutThreadedSearch ());
			}

		}

		// Note: called outside of Unity main thread
		void TimeOutThreadedSearch () {
			if (cancelSearchTimer == null || !cancelSearchTimer.IsCancellationRequested) {
				search.EndSearch ();
			}
		}
		//Cancel tìm kiếm Thread
		void PlayBookMove(Move bookMove) {
			this.move = bookMove;
			moveFound = true;
		}
		//Áp dụng bookmove thu được vào move tiếp theo
		void OnSearchComplete (Move move) {
			// Cancel search timer in case search finished before timer ran out (can happen when a mate is found)
			cancelSearchTimer?.Cancel ();
			moveFound = true;
			this.move = move;
		}
	}
}