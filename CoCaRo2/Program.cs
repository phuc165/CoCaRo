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
    // Common code shared between server and client
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

    // Server Application
    public class CaroServer : Form
    {
        private TcpListener server;
        private List<TcpClient> clients = new List<TcpClient>();
        private Common.CellState[,] board = new Common.CellState[Common.BOARD_SIZE, Common.BOARD_SIZE];
        private Common.GameStatus gameStatus = Common.GameStatus.Waiting;
        private int currentPlayerIndex = 0;
        private Button startButton;
        private Label statusLabel;
        private int connectedClients = 0;

        public CaroServer()
        {
            InitializeComponents();
            InitializeBoard();
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

                foreach (var client in clients)
                {
                    try
                    {
                        client.Close();
                    }
                    catch { }
                }

                clients.Clear();
                gameStatus = Common.GameStatus.Waiting;
                connectedClients = 0;
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

                    if (connectedClients < 2)
                    {
                        clients.Add(client);
                        connectedClients++;

                        Thread clientThread = new Thread(() => HandleClient(client, connectedClients - 1));
                        clientThread.IsBackground = true;
                        clientThread.Start();

                        UpdateStatus($"Client {connectedClients} connected. Waiting for {2 - connectedClients} more players.");

                        // Assign player role (X or O)
                        NetworkStream stream = client.GetStream();
                        string role = (connectedClients == 1) ? "X" : "O";
                        byte[] roleMsg = Encoding.ASCII.GetBytes(Common.FormatMessage("ROLE", role));
                        stream.Write(roleMsg, 0, roleMsg.Length);

                        if (connectedClients == 2)
                        {
                            gameStatus = Common.GameStatus.Playing;
                            BroadcastMessage(Common.FormatMessage("START", ""));
                            UpdateStatus("Game started. Player X's turn.");
                        }
                    }
                    else
                    {
                        // Reject additional connections
                        NetworkStream stream = client.GetStream();
                        byte[] rejectMsg = Encoding.ASCII.GetBytes(Common.FormatMessage("REJECT", "Game full"));
                        stream.Write(rejectMsg, 0, rejectMsg.Length);
                        client.Close();
                    }
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

        private void HandleClient(TcpClient client, int playerIndex)
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

                    if (command == "MOVE" && gameStatus == Common.GameStatus.Playing && currentPlayerIndex == playerIndex)
                    {
                        string[] coords = data.Split(',');
                        int row = int.Parse(coords[0]);
                        int col = int.Parse(coords[1]);

                        if (row >= 0 && row < Common.BOARD_SIZE &&
                            col >= 0 && col < Common.BOARD_SIZE &&
                            board[row, col] == Common.CellState.Empty)
                        {
                            // Make the move
                            board[row, col] = (playerIndex == 0) ? Common.CellState.X : Common.CellState.O;

                            // Broadcast the move to all clients
                            BroadcastMessage(Common.FormatMessage("MOVE", $"{row},{col},{playerIndex}"));

                            // Check for win
                            if (CheckWin(row, col))
                            {
                                gameStatus = Common.GameStatus.GameOver;
                                BroadcastMessage(Common.FormatMessage("GAMEOVER", $"Player {((playerIndex == 0) ? "X" : "O")} wins!"));
                                UpdateStatus($"Game over. Player {((playerIndex == 0) ? "X" : "O")} wins!");
                            }
                            else if (IsBoardFull())
                            {
                                gameStatus = Common.GameStatus.GameOver;
                                BroadcastMessage(Common.FormatMessage("GAMEOVER", "Draw!"));
                                UpdateStatus("Game over. It's a draw!");
                            }
                            else
                            {
                                // Switch to the other player
                                currentPlayerIndex = 1 - currentPlayerIndex;
                                UpdateStatus($"Player {((currentPlayerIndex == 0) ? "X" : "O")}'s turn");
                            }
                        }
                    }
                    else if (command == "CHAT")
                    {
                        // Handle chat message
                        BroadcastMessage(Common.FormatMessage("CHAT", $"Player {playerIndex + 1}: {data}"));
                    }
                    else if (command == "RESTART" && gameStatus == Common.GameStatus.GameOver)
                    {
                        InitializeBoard();
                        gameStatus = Common.GameStatus.Playing;
                        currentPlayerIndex = 0;
                        BroadcastMessage(Common.FormatMessage("RESTART", ""));
                        UpdateStatus("Game restarted. Player X's turn.");
                    }
                }
            }
            catch (Exception)
            {
                // Client disconnected
                if (clients.Contains(client))
                {
                    clients.Remove(client);
                    connectedClients--;

                    UpdateStatus($"Client disconnected. {connectedClients} clients connected.");
                    BroadcastMessage(Common.FormatMessage("DISCONNECT", $"Player {playerIndex + 1} disconnected"));

                    if (gameStatus == Common.GameStatus.Playing)
                    {
                        gameStatus = Common.GameStatus.Waiting;
                    }

                    try
                    {
                        client.Close();
                    }
                    catch { }
                }
            }
        }

        private void BroadcastMessage(string message)
        {
            byte[] data = Encoding.ASCII.GetBytes(message);

            foreach (var client in clients)
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

        private void InitializeBoard()
        {
            for (int i = 0; i < Common.BOARD_SIZE; i++)
            {
                for (int j = 0; j < Common.BOARD_SIZE; j++)
                {
                    board[i, j] = Common.CellState.Empty;
                }
            }
        }

        private bool CheckWin(int row, int col)
        {
            Common.CellState player = board[row, col];

            // Check horizontally
            int count = 0;
            for (int c = Math.Max(0, col - 4); c <= Math.Min(Common.BOARD_SIZE - 1, col + 4); c++)
            {
                if (board[row, c] == player)
                {
                    count++;
                    if (count == Common.WINNING_COUNT) return true;
                }
                else
                {
                    count = 0;
                }
            }

            // Check vertically
            count = 0;
            for (int r = Math.Max(0, row - 4); r <= Math.Min(Common.BOARD_SIZE - 1, row + 4); r++)
            {
                if (board[r, col] == player)
                {
                    count++;
                    if (count == Common.WINNING_COUNT) return true;
                }
                else
                {
                    count = 0;
                }
            }

            // Check diagonal (top-left to bottom-right)
            count = 0;
            for (int i = -4; i <= 4; i++)
            {
                int r = row + i;
                int c = col + i;
                if (r >= 0 && r < Common.BOARD_SIZE && c >= 0 && c < Common.BOARD_SIZE)
                {
                    if (board[r, c] == player)
                    {
                        count++;
                        if (count == Common.WINNING_COUNT) return true;
                    }
                    else
                    {
                        count = 0;
                    }
                }
            }

            // Check diagonal (top-right to bottom-left)
            count = 0;
            for (int i = -4; i <= 4; i++)
            {
                int r = row + i;
                int c = col - i;
                if (r >= 0 && r < Common.BOARD_SIZE && c >= 0 && c < Common.BOARD_SIZE)
                {
                    if (board[r, c] == player)
                    {
                        count++;
                        if (count == Common.WINNING_COUNT) return true;
                    }
                    else
                    {
                        count = 0;
                    }
                }
            }

            return false;
        }

        private bool IsBoardFull()
        {
            for (int i = 0; i < Common.BOARD_SIZE; i++)
            {
                for (int j = 0; j < Common.BOARD_SIZE; j++)
                {
                    if (board[i, j] == Common.CellState.Empty)
                    {
                        return false;
                    }
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

    // Client Application
    
}