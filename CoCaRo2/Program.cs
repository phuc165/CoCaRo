using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CaRoServer
{
    public class Common
    {
        public const int BOARD_SIZE = 15;
        public const int CELL_SIZE = 40;
        public const int WINNING_COUNT = 5;

        public enum CellState { Empty, X, O }
        public enum GameStatus { Waiting, Playing, GameOver }

        public static string FormatMessage(string command, string data)
        {
            return $"{command}|{data}";
        }

        public static void ParseMessage(string message, out string command, out string data)
        {
            string[] parts = message.Split('|');
            command = parts[0];
            data = parts.Length > 1 ? parts[1] : "";
        }
    }

    public class Room
    {
        public List<TcpClient> Players { get; set; } = new List<TcpClient>();
        public Common.CellState[,] Board { get; set; } = new Common.CellState[Common.BOARD_SIZE, Common.BOARD_SIZE];
        public Common.GameStatus GameStatus { get; set; } = Common.GameStatus.Waiting;
        public int CurrentPlayerIndex { get; set; } = 0;
        public int RoomId { get; set; }
        public bool[] RematchRequests { get; set; } = new bool[2];

        public Room(int id)
        {
            RoomId = id;
            InitializeBoard();
        }

        public void InitializeBoard()
        {
            for (int i = 0; i < Common.BOARD_SIZE; i++)
            {
                for (int j = 0; j < Common.BOARD_SIZE; j++)
                {
                    Board[i, j] = Common.CellState.Empty;
                }
            }
        }
    }

    public class CaroServer : Form
    {
        private TcpListener server;
        private List<Room> rooms = new List<Room>();
        private Button startButton;
        private Label statusLabel;

        public CaroServer()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "Caro Game Server";
            this.Size = new Size(400, 300);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            statusLabel = new Label
            {
                Text = "Server Status: Not Running",
                Location = new Point(20, 20),
                AutoSize = true
            };

            startButton = new Button
            {
                Text = "Start Server",
                Location = new Point(20, 50),
                Size = new Size(100, 30)
            };
            startButton.Click += StartServer_Click;

            this.Controls.Add(statusLabel);
            this.Controls.Add(startButton);

            this.FormClosing += (s, e) => StopServer();
        }

        private void StartServer_Click(object sender, EventArgs e)
        {
            if (server == null)
            {
                StartServer();
                startButton.Text = "Stop Server";
            }
            else
            {
                StopServer();
                startButton.Text = "Start Server";
            }
        }

        private void StartServer()
        {
            try
            {
                server = new TcpListener(IPAddress.Any, 8888);
                server.Start();

                statusLabel.Text = "Server Status: Running on port 8888";

                Thread listenerThread = new Thread(ListenForClients);
                listenerThread.IsBackground = true;
                listenerThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting server: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopServer()
        {
            if (server != null)
            {
                server.Stop();
                server = null;

                foreach (var room in rooms)
                {
                    foreach (var client in room.Players)
                    {
                        try { client.Close(); } catch { }
                    }
                }

                rooms.Clear();
                statusLabel.Text = "Server Status: Not Running";
            }
        }

        private void ListenForClients()
        {
            try
            {
                while (true)
                {
                    TcpClient client = server.AcceptTcpClient();
                    AssignClientToRoom(client);
                }
            }
            catch (SocketException)
            {
                // Server stopped
            }
            catch (Exception ex)
            {
                UpdateStatus($"Server error: {ex.Message}");
            }
        }

        private void AssignClientToRoom(TcpClient client)
        {
            Room availableRoom = null;

            foreach (var room in rooms)
            {
                if (room.Players.Count < 2 && room.GameStatus == Common.GameStatus.Waiting)
                {
                    availableRoom = room;
                    break;
                }
            }

            if (availableRoom == null)
            {
                availableRoom = new Room(rooms.Count);
                rooms.Add(availableRoom);
            }

            int playerIndex = availableRoom.Players.Count;
            availableRoom.Players.Add(client);

            UpdateStatus($"Player joined Room {availableRoom.RoomId}. Players: {availableRoom.Players.Count}/2");

            Thread clientThread = new Thread(() => HandleClient(client, availableRoom, playerIndex));
            clientThread.IsBackground = true;
            clientThread.Start();

            NetworkStream stream = client.GetStream();
            string role = (playerIndex == 0) ? "X" : "O";
            byte[] roleMsg = Encoding.ASCII.GetBytes(Common.FormatMessage("ROLE", $"{role},{availableRoom.RoomId}"));
            stream.Write(roleMsg, 0, roleMsg.Length);

            if (availableRoom.Players.Count == 2)
            {
                availableRoom.GameStatus = Common.GameStatus.Playing;
                BroadcastToRoom(availableRoom, Common.FormatMessage("START", ""));
                UpdateStatus($"Room {availableRoom.RoomId}: Game started. Player X's turn.");
            }
        }

        private void HandleClient(TcpClient client, Room room, int playerIndex)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];

            try
            {
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    string command, data;
                    Common.ParseMessage(message, out command, out data);

                    if (command == "MOVE" && room.GameStatus == Common.GameStatus.Playing && room.CurrentPlayerIndex == playerIndex)
                    {
                        string[] coords = data.Split(',');
                        int row = int.Parse(coords[0]);
                        int col = int.Parse(coords[1]);

                        if (row >= 0 && row < Common.BOARD_SIZE && col >= 0 && col < Common.BOARD_SIZE &&
                            room.Board[row, col] == Common.CellState.Empty)
                        {
                            room.Board[row, col] = (playerIndex == 0) ? Common.CellState.X : Common.CellState.O;
                            BroadcastToRoom(room, Common.FormatMessage("MOVE", $"{row},{col},{playerIndex}"));

                            if (CheckWin(room, row, col))
                            {
                                room.GameStatus = Common.GameStatus.GameOver;
                                BroadcastToRoom(room, Common.FormatMessage("GAMEOVER", $"Player {((playerIndex == 0) ? "X" : "O")} wins!"));
                                UpdateStatus($"Room {room.RoomId}: Player {((playerIndex == 0) ? "X" : "O")} wins!");
                            }
                            else if (IsBoardFull(room))
                            {
                                room.GameStatus = Common.GameStatus.GameOver;
                                BroadcastToRoom(room, Common.FormatMessage("GAMEOVER", "Draw!"));
                                UpdateStatus($"Room {room.RoomId}: Draw!");
                            }
                            else
                            {
                                room.CurrentPlayerIndex = 1 - room.CurrentPlayerIndex;
                                UpdateStatus($"Room {room.RoomId}: Player {((room.CurrentPlayerIndex == 0) ? "X" : "O")}'s turn");
                            }
                        }
                    }
                    else if (command == "CHAT")
                    {
                        BroadcastToRoom(room, Common.FormatMessage("CHAT", $"Player {playerIndex + 1}: {data}"));
                    }
                    else if (command == "SURRENDER")
                    {
                        room.GameStatus = Common.GameStatus.GameOver;
                        string surrenderMsg = $"Player {((playerIndex == 0) ? "X" : "O")} surrendered";
                        BroadcastToRoom(room, Common.FormatMessage("GAMEOVER", surrenderMsg));
                        UpdateStatus($"Room {room.RoomId}: {surrenderMsg}");
                    }
                    else if (command == "REMATCH" && room.GameStatus == Common.GameStatus.GameOver)
                    {
                        room.RematchRequests[playerIndex] = true;
                        if (room.RematchRequests[0] && room.RematchRequests[1])
                        {
                            room.InitializeBoard();
                            room.GameStatus = Common.GameStatus.Playing;
                            room.CurrentPlayerIndex = 0;
                            room.RematchRequests[0] = false;
                            room.RematchRequests[1] = false;
                            BroadcastToRoom(room, Common.FormatMessage("RESTART", ""));
                            UpdateStatus($"Room {room.RoomId}: Game restarted. Player X's turn.");
                        }
                    }
                }
            }
            catch (Exception)
            {
                if (room.Players.Contains(client))
                {
                    room.Players.Remove(client);
                    UpdateStatus($"Room {room.RoomId}: Player disconnected. {room.Players.Count}/2 players.");
                    BroadcastToRoom(room, Common.FormatMessage("DISCONNECT", $"Player {playerIndex + 1} disconnected"));

                    if (room.GameStatus == Common.GameStatus.Playing)
                    {
                        room.GameStatus = Common.GameStatus.Waiting;
                    }

                    try { client.Close(); } catch { }
                }
            }
        }

        private void BroadcastToRoom(Room room, string message)
        {
            byte[] data = Encoding.ASCII.GetBytes(message);
            foreach (var client in room.Players)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    stream.Write(data, 0, data.Length);
                }
                catch { }
            }
        }

        private void UpdateStatus(string status)
        {
            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke(new Action<string>(UpdateStatus), status);
            }
            else
            {
                statusLabel.Text = status;
            }
        }

        private bool CheckWin(Room room, int row, int col)
        {
            Common.CellState player = room.Board[row, col];
            int count;

            // Horizontal
            count = 0;
            for (int c = Math.Max(0, col - 4); c <= Math.Min(Common.BOARD_SIZE - 1, col + 4); c++)
            {
                if (room.Board[row, c] == player) { count++; if (count == Common.WINNING_COUNT) return true; } else { count = 0; }
            }

            // Vertical
            count = 0;
            for (int r = Math.Max(0, row - 4); r <= Math.Min(Common.BOARD_SIZE - 1, row + 4); r++)
            {
                if (room.Board[r, col] == player) { count++; if (count == Common.WINNING_COUNT) return true; } else { count = 0; }
            }

            // Diagonal (top-left to bottom-right)
            count = 0;
            for (int i = -4; i <= 4; i++)
            {
                int r = row + i;
                int c = col + i;
                if (r >= 0 && r < Common.BOARD_SIZE && c >= 0 && c < Common.BOARD_SIZE)
                {
                    if (room.Board[r, c] == player) { count++; if (count == Common.WINNING_COUNT) return true; } else { count = 0; }
                }
            }

            // Diagonal (top-right to bottom-left)
            count = 0;
            for (int i = -4; i <= 4; i++)
            {
                int r = row + i;
                int c = col - i;
                if (r >= 0 && r < Common.BOARD_SIZE && c >= 0 && c < Common.BOARD_SIZE)
                {
                    if (room.Board[r, c] == player) { count++; if (count == Common.WINNING_COUNT) return true; } else { count = 0; }
                }
            }

            return false;
        }

        private bool IsBoardFull(Room room)
        {
            for (int i = 0; i < Common.BOARD_SIZE; i++)
            {
                for (int j = 0; j < Common.BOARD_SIZE; j++)
                {
                    if (room.Board[i, j] == Common.CellState.Empty) return false;
                }
            }
            return true;
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CaroServer());
        }
    }
}