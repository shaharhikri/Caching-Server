using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CacheServer
{
    class Program
    {
        public static void Main()
        {
            MyServer myServer = new MyServer(10011, 128000000); //PORT 10011, 128 MegaBytes
            Console.Title = "Cache Server";
            myServer.SetupServer();
            Console.ReadLine(); // When we press enter close everything
            myServer.CloseAllSockets();
        }
    }

    class MyServer
    {
        private readonly Socket _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private readonly List<Socket> _clientSockets;

        private Dictionary<Socket, string> _clientMsgs;
        private Dictionary<Socket, bool> _duringSetCommand;
        private ConcurrentDictionary<string, byte[]> _chacheDict;

        private int _values_size;
        private int _max_values_size;

        private readonly byte[] _char_buffer;
        private const int CHAR_BUFFER_SIZE = 2;
        private int _port;

        private Mutex _values_size_mutex;

        public MyServer(int port, int max_values_size)
        {
            this._serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            this._clientSockets = new List<Socket>();

            this._clientMsgs = new Dictionary<Socket, string>();
            this._duringSetCommand = new Dictionary<Socket, bool>();
            this._chacheDict = new ConcurrentDictionary<string, byte[]>();

            this._values_size = 0;
            this._max_values_size = max_values_size;

            this._char_buffer = new byte[CHAR_BUFFER_SIZE];
            this._port = port;

            _values_size_mutex = new Mutex();
        }

        public void SetupServer()
        {
            Console.WriteLine("Setting up server...");
            _serverSocket.Bind(new IPEndPoint(IPAddress.Any, _port));
            _serverSocket.Listen(0);
            _serverSocket.BeginAccept(AcceptCallback, null);
            Console.WriteLine("Server setup complete");
        }

        private void AcceptCallback(IAsyncResult ar)
        {
            Socket clientSocket = _serverSocket.EndAccept(ar);

            _clientSockets.Add(clientSocket);
            _clientMsgs.Add(clientSocket, "");
            _duringSetCommand.Add(clientSocket, false);

            clientSocket.BeginReceive(_char_buffer, 0, CHAR_BUFFER_SIZE, SocketFlags.None, ReceiveCallback, clientSocket);
            Console.WriteLine("Client connected, waiting for request... " + clientSocket);
            _serverSocket.BeginAccept(AcceptCallback, null);
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            Socket clientSocket = (Socket)ar.AsyncState;
            int received;
            try
            {
                received = clientSocket.EndReceive(ar);
            }
            catch (SocketException)
            {
                CloseSocket(clientSocket);
                return;
            }

            byte[] recBuf = new byte[received];
            Array.Copy(_char_buffer, recBuf, received);
            string text = Encoding.UTF8.GetString(recBuf);

            if (text == "")
            {
                CloseSocket(clientSocket);
                return;
            }
            else if (text == "\r\n")
            {
                string[] command = SplitBySpaces(_clientMsgs[clientSocket]);

                if (_duringSetCommand[clientSocket])
                {
                    string key = command[1];
                    int val_len = int.Parse(command[2]);
                    string val = "";
                    for (int i = 3; i < command.Length - 1; i++)
                    {
                        val += command[i] + " ";
                    }
                    val += command[command.Length - 1];
                    setCommand(key, val, val_len);
                    clientSocket.Send(Encoding.UTF8.GetBytes("OK\r\n"));
                    _duringSetCommand[clientSocket] = false;
                }
                else if (command.Length == 2 && command[0] == "get")
                {
                    string key = command[1];
                    byte[] bytes_val;
                    if(_chacheDict.TryGetValue(key, out bytes_val))
                    {
                        string val = Encoding.UTF8.GetString(bytes_val);
                        clientSocket.Send(Encoding.UTF8.GetBytes("OK " + val.Length + "\r\n" + val + "\r\n"));
                    }
                    else
                    {
                        clientSocket.Send(Encoding.UTF8.GetBytes("MISSING\r\n"));
                    }
                }
                else if (command.Length == 3 && command[0] == "set")
                {
                    _clientMsgs[clientSocket] = (_clientMsgs[clientSocket] + " ");
                    _duringSetCommand[clientSocket] = true;
                }
                else
                {
                    clientSocket.Send(Encoding.UTF8.GetBytes("Invalid input\r\n")); //invalid input
                }

                if (!_duringSetCommand[clientSocket])
                    _clientMsgs[clientSocket] = "";
            }
            else
            {
                _clientMsgs[clientSocket] = (_clientMsgs[clientSocket] + text);
            }

            //Console.WriteLine("text_len = " + text.Length + " ,SequenceReceived: " + clientMsgs[socket]);
            clientSocket.BeginReceive(_char_buffer, 0, CHAR_BUFFER_SIZE, SocketFlags.None, ReceiveCallback, clientSocket);
        }

        /*Perform "set" command by client.
          Add/Update (size fixed) value by key and clean some old entities from cache 
          if it's values size reaches to max size
          - Thread safe action */
        private void setCommand(string key, string val, int val_len)
        {
            if (val_len < val.Length)
            {
                val = val.Substring(0, val_len);
            }
            else if (val_len > val.Length)
            {
                int spaces_len = val_len - val.Length;
                for (int i = 0; i < spaces_len; i++)
                {
                    val = val + " ";
                }
            }

            byte[] bytes_val = Encoding.UTF8.GetBytes(val);
            int old_val_len = 0;
            _chacheDict.AddOrUpdate(key, bytes_val, (key, existingVal) => {
                old_val_len = existingVal.Length;
                return bytes_val; 
            });


            _values_size_mutex.WaitOne(); //open critical section ********
            _values_size = _values_size - old_val_len + val_len;
            //clean cache (size) - clean some old entities from cache if it's values size reaches to max size
            if (_values_size > _max_values_size)
            {
                List<string> keys = new List<string>(_chacheDict.Keys); //Ordered by last set time

                foreach (string k in keys)
                {
                    _values_size -= _chacheDict[k].Length;
                    byte[] removed_val;
                    _chacheDict.TryRemove(k, out removed_val);
                    Console.WriteLine("Removed key:" + k);
                    if (_values_size <= (_max_values_size / 2))
                        break;
                }
            }
            _values_size_mutex.ReleaseMutex(); //close critical section ****
        }

        /*  Split string by spaces
            for example:   "a  bb       ccc ddd "  =>   {"a","bb","ccc","ddd"}  */
        private string[] SplitBySpaces(string s)
        {
            string[] subs = s.Split(' ');
            List<string> subs_lst = new List<string>();
            for (int i = 0; i < subs.Length; i++)
            {
                subs[i] = subs[i].Trim();
                if (subs[i].Length > 0)
                    subs_lst.Add(subs[i]);
            }
            return subs_lst.ToArray();
        }

        /*Close 1 socket*/
        private void CloseSocket(Socket clientSocket)
        {
            Console.WriteLine("Client forcefully disconnected");
            // Don't shutdown because the socket may be disposed and its disconnected anyway.
            _clientSockets.Remove(clientSocket);
            _clientMsgs.Remove(clientSocket);
            _duringSetCommand.Remove(clientSocket);
            clientSocket.Close();
        }

        /*Close all connections*/
        public void CloseAllSockets()
        {
            foreach (Socket cs in _clientSockets)
            {
                cs.Shutdown(SocketShutdown.Both);
                CloseSocket(cs);
            }
            _serverSocket.Close();
        }

    }
}