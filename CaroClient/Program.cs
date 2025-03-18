using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CaroClient
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

    public class CaroClient : Form
    {
        private TcpClient client;
        private NetworkStream stream;
        private Thread listenThread;
        private Panel boardPanel;
        private Label statusLabel;
        private Label roomLabel;
        private TextBox chatInput;
        private Button sendButton;
        private ListBox chatBox;
        private Button connectButton;
        private TextBox serverInput;
        private Common.CellState[,] board = new Common.CellState[Common.BOARD_SIZE, Common.BOARD_SIZE];
        private Common.GameStatus gameStatus = Common.GameStatus.Waiting;
        private Common.CellState playerRole = Common.CellState.Empty;
        private bool isMyTurn = false;
        private int roomId = -1;
        private int lastMoveRow = -1; // For highlighting latest move
        private int lastMoveCol = -1; // For highlighting latest move

        public CaroClient()
        {
            InitializeComponents();
            InitializeBoard();
        }

        private void InitializeComponents()
        {
            this.Text = "Caro Game Client";
            this.Size = new Size(800, 700);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            // Server connection controls
            Label serverLabel = new Label
            {
                Text = "Server Address:",
                Location = new Point(20, 20),
                AutoSize = true
            };

            serverInput = new TextBox
            {
                Text = "127.0.0.1",
                Location = new Point(120, 20),
                Size = new Size(150, 20)
            };

            connectButton = new Button
            {
                Text = "Connect",
                Location = new Point(280, 19),
                Size = new Size(80, 23)
            };
            connectButton.Click += Connect_Click;

            statusLabel = new Label
            {
                Text = "Not connected",
                Location = new Point(20, 50),
                Size = new Size(500, 20)
            };

            roomLabel = new Label
            {
                Text = "Room: None",
                Location = new Point(20, 70),
                Size = new Size(200, 20)
            };

            // Game board
            boardPanel = new Panel
            {
                Location = new Point(20, 100),
                Size = new Size(Common.BOARD_SIZE * Common.CELL_SIZE, Common.BOARD_SIZE * Common.CELL_SIZE),
                BorderStyle = BorderStyle.FixedSingle
            };
            boardPanel.Paint += BoardPanel_Paint;
            boardPanel.MouseClick += BoardPanel_MouseClick;

            // Chat controls
            chatBox = new ListBox
            {
                Location = new Point(Common.BOARD_SIZE * Common.CELL_SIZE + 50, 100),
                Size = new Size(250, 400),
                IntegralHeight = false
            };

            chatInput = new TextBox
            {
                Location = new Point(Common.BOARD_SIZE * Common.CELL_SIZE + 50, 510),
                Size = new Size(170, 20)
            };
            chatInput.KeyPress += (s, e) => { if (e.KeyChar == (char)13) SendChatMessage(); };

            sendButton = new Button
            {
                Text = "Send",
                Location = new Point(Common.BOARD_SIZE * Common.CELL_SIZE + 230, 509),
                Size = new Size(70, 23)
            };
            sendButton.Click += (s, e) => SendChatMessage();

            // Surrender button
            Button surrenderButton = new Button
            {
                Text = "Surrender",
                Location = new Point(Common.BOARD_SIZE * Common.CELL_SIZE + 50, 540),
                Size = new Size(100, 30)
            };
            surrenderButton.Click += Surrender_Click;

            this.Controls.Add(serverLabel);
            this.Controls.Add(serverInput);
            this.Controls.Add(connectButton);
            this.Controls.Add(statusLabel);
            this.Controls.Add(roomLabel);
            this.Controls.Add(boardPanel);
            this.Controls.Add(chatBox);
            this.Controls.Add(chatInput);
            this.Controls.Add(sendButton);
            this.Controls.Add(surrenderButton);

            this.FormClosing += (s, e) => DisconnectFromServer();
        }

        private void Connect_Click(object sender, EventArgs e)
        {
            if (client == null)
            {
                ConnectToServer();
            }
            else
            {
                DisconnectFromServer();
            }
        }

        private void ConnectToServer()
        {
            try
            {
                string serverAddress = serverInput.Text.Trim();
                client = new TcpClient();
                client.Connect(serverAddress, 8888);

                stream = client.GetStream();

                listenThread = new Thread(ListenForServerMessages);
                listenThread.IsBackground = true;
                listenThread.Start();

                statusLabel.Text = "Connected to server. Waiting for room assignment...";
                connectButton.Text = "Disconnect";

                chatInput.Enabled = true;
                sendButton.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error connecting to server: {ex.Message}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DisconnectFromServer()
        {
            if (client != null)
            {
                try
                {
                    if (listenThread != null && listenThread.IsAlive)
                    {
                        listenThread.Abort();
                    }

                    if (stream != null)
                    {
                        stream.Close();
                    }

                    client.Close();
                }
                catch { }

                client = null;
                stream = null;

                gameStatus = Common.GameStatus.Waiting;
                playerRole = Common.CellState.Empty;
                isMyTurn = false;
                roomId = -1;

                statusLabel.Text = "Disconnected from server";
                connectButton.Text = "Connect";
                roomLabel.Text = "Room: None";

                chatInput.Enabled = false;
                sendButton.Enabled = false;

                InitializeBoard();
                boardPanel.Invalidate();
            }
        }

        private void ListenForServerMessages()
        {
            byte[] buffer = new byte[1024];

            try
            {
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    string command, data;
                    Common.ParseMessage(message, out command, out data);

                    if (command == "ROLE")
                    {
                        string[] parts = data.Split(',');
                        string role = parts[0];
                        roomId = int.Parse(parts[1]);

                        playerRole = (role == "X") ? Common.CellState.X : Common.CellState.O;
                        UpdateRoomLabel($"Room: {roomId}");
                        UpdateStatus($"You are playing as {role} in Room {roomId}. Waiting for another player...");
                    }
                    else if (command == "START")
                    {
                        gameStatus = Common.GameStatus.Playing;
                        isMyTurn = (playerRole == Common.CellState.X);
                        UpdateStatus(isMyTurn ? "Game started. Your turn!" : "Game started. Opponent's turn...");
                    }
                    else if (command == "MOVE")
                    {
                        string[] parts = data.Split(',');
                        int row = int.Parse(parts[0]);
                        int col = int.Parse(parts[1]);
                        int playerIndex = int.Parse(parts[2]);

                        board[row, col] = (playerIndex == 0) ? Common.CellState.X : Common.CellState.O;
                        lastMoveRow = row;
                        lastMoveCol = col;
                        isMyTurn = (playerIndex == 0 && playerRole == Common.CellState.O) ||
                                  (playerIndex == 1 && playerRole == Common.CellState.X);

                        UpdateStatus(isMyTurn ? "Your turn!" : "Opponent's turn...");
                        UpdateBoard();
                    }
                    else if (command == "GAMEOVER")
                    {
                        gameStatus = Common.GameStatus.GameOver;
                        UpdateStatus($"Game over: {data}");

                        DialogResult result = MessageBox.Show($"Game over: {data}\nDo you want to rematch?", "Game Over",
                                                              MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (result == DialogResult.Yes)
                        {
                            SendMessage(Common.FormatMessage("REMATCH", ""));
                        }
                        else
                        {
                            SendMessage(Common.FormatMessage("NO_REMATCH", ""));
                        }
                    }
                    else if (command == "RESTART")
                    {
                        InitializeBoard();
                        lastMoveRow = -1;
                        lastMoveCol = -1;
                        gameStatus = Common.GameStatus.Playing;
                        isMyTurn = (playerRole == Common.CellState.X);
                        UpdateStatus(isMyTurn ? "Game restarted. Your turn!" : "Game restarted. Opponent's turn...");
                        UpdateBoard();
                    }
                    else if (command == "CHAT")
                    {
                        AddChatMessage(data);
                    }
                    else if (command == "DISCONNECT")
                    {
                        gameStatus = Common.GameStatus.Waiting;
                        UpdateStatus($"{data}. Waiting for reconnection...");
                    }
                    else if (command == "END_GAME")
                    {
                        MessageBox.Show(data, "Game Ended", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        DisconnectFromServer();
                    }
                }
            }
            catch (ThreadAbortException)
            {
                // Thread was aborted
            }
            catch (Exception)
            {
                if (this.IsHandleCreated)
                {
                    this.Invoke(new Action(() =>
                    {
                        if (client != null)
                        {
                            DisconnectFromServer();
                            MessageBox.Show("Connection to server lost.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }));
                }
            }
        }

        private void BoardPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            for (int i = 0; i <= Common.BOARD_SIZE; i++)
            {
                g.DrawLine(Pens.Black, 0, i * Common.CELL_SIZE, Common.BOARD_SIZE * Common.CELL_SIZE, i * Common.CELL_SIZE);
                g.DrawLine(Pens.Black, i * Common.CELL_SIZE, 0, i * Common.CELL_SIZE, Common.BOARD_SIZE * Common.CELL_SIZE);
            }

            for (int row = 0; row < Common.BOARD_SIZE; row++)
            {
                for (int col = 0; col < Common.BOARD_SIZE; col++)
                {
                    int x = col * Common.CELL_SIZE;
                    int y = row * Common.CELL_SIZE;

                    if (board[row, col] == Common.CellState.X)
                    {
                        Pen pen = (row == lastMoveRow && col == lastMoveCol) ? new Pen(Color.Green, 2) : new Pen(Color.Blue, 2);
                        g.DrawLine(pen, x + 5, y + 5, x + Common.CELL_SIZE - 5, y + Common.CELL_SIZE - 5);
                        g.DrawLine(pen, x + Common.CELL_SIZE - 5, y + 5, x + 5, y + Common.CELL_SIZE - 5);
                    }
                    else if (board[row, col] == Common.CellState.O)
                    {
                        Pen pen = (row == lastMoveRow && col == lastMoveCol) ? new Pen(Color.Green, 2) : new Pen(Color.Red, 2);
                        g.DrawEllipse(pen, x + 5, y + 5, Common.CELL_SIZE - 10, Common.CELL_SIZE - 10);
                    }
                }
            }
        }

        private void BoardPanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (gameStatus == Common.GameStatus.Playing && isMyTurn)
            {
                int col = e.X / Common.CELL_SIZE;
                int row = e.Y / Common.CELL_SIZE;

                if (row >= 0 && row < Common.BOARD_SIZE && col >= 0 && col < Common.BOARD_SIZE &&
                    board[row, col] == Common.CellState.Empty)
                {
                    SendMessage(Common.FormatMessage("MOVE", $"{row},{col}"));
                }
            }
        }

        private void Surrender_Click(object sender, EventArgs e)
        {
            if (gameStatus == Common.GameStatus.Playing)
            {
                SendMessage(Common.FormatMessage("SURRENDER", ""));
            }
        }

        private void SendChatMessage()
        {
            string message = chatInput.Text.Trim();
            if (!string.IsNullOrEmpty(message) && client != null && stream != null)
            {
                SendMessage(Common.FormatMessage("CHAT", message));
                chatInput.Text = "";
            }
        }

        private void SendMessage(string message)
        {
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(message);
                stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending message: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddChatMessage(string message)
        {
            if (chatBox.InvokeRequired)
            {
                chatBox.Invoke(new Action<string>(AddChatMessage), message);
            }
            else
            {
                chatBox.Items.Add(message);
                chatBox.SelectedIndex = chatBox.Items.Count - 1;
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

        private void UpdateRoomLabel(string text)
        {
            if (roomLabel.InvokeRequired)
            {
                roomLabel.Invoke(new Action<string>(UpdateRoomLabel), text);
            }
            else
            {
                roomLabel.Text = text;
            }
        }

        private void UpdateBoard()
        {
            if (boardPanel.InvokeRequired)
            {
                boardPanel.Invoke(new Action(UpdateBoard));
            }
            else
            {
                boardPanel.Invalidate();
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

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CaroClient());
        }
    }
}