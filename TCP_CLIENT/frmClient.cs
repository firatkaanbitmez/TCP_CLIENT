using CommonClassLibs;
using MaterialSkin;
using MaterialSkin.Controls;
using Microsoft.VisualBasic.Devices;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tulpep.NotificationWindow;
using Squirrel;
using System.Diagnostics;

namespace TCP_CLIENT
{
    public partial class frmClient : MaterialForm
    {
        //cli Socket class
        private Client client = null;

        private Dictionary<int, UserClients> dUserClientsList = null;
                
        /// Her istemci, gelen metin bilgilerini takip etmek için kendi StringBuilder'ını alır.
        
        private Dictionary<int, StringBuilder> textInformation = new Dictionary<int, StringBuilder>();

        private List<FilesToSend> FilesToSendList = null;

        private Progress ProgressBarClass = null;//progress bar(only need one)

        private string MyDocumentsPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        
        private MotherOfRawPackets HostServerRawPackets = null;
        static AutoResetEvent autoEventHostServer = null;//mutex
        static AutoResetEvent autoEvent2;//mutex
        private Thread DataProcessHostServerThread = null;
        private Thread FullPacketDataProcessThread = null;
        private Queue<FullPacket> FullHostServerPacketList = null;
        
        private Dictionary<int, FileBody> dFileBodyList = null;
        

        bool AppIsExiting = false;
        bool ServerConnected = false;
        int MyHostServerID = 0;
        long ServerTime = DateTime.Now.Ticks;

        System.Windows.Forms.Timer GeneralTimer = null;

        public frmClient()
        {
            InitializeComponent();
            var materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.LIGHT;
            materialSkinManager.ColorScheme = new ColorScheme(Primary.BlueGrey800, Primary.BlueGrey900, Primary.BlueGrey500, Accent.LightBlue200, TextShade.WHITE);
        }
        public string ad, soyad, okulno, svIP, svPort; //  login information
        private void frmClient_Load(object sender, EventArgs e)
        {


            AddVersionNumber();
            CheckForUpdates();


            //create directorty
            CheckOnApplicationDirectory();                       
            pictureBox1.Image = Properties.Resources.offline;

            dUserClientsList = new Dictionary<int, UserClients>();
            dFileBodyList = new Dictionary<int, FileBody>();
                        

            ServerConnected = true;//server connect LED Main login olurken servera bağlanması için 
            InitializeServerConnection();
            if (ConnectToHostServer())
            {
                ServerConnected = true;
                
                
                //Login butonu
                panel1.AllowDrop = true;
                buttonSendToClients.Enabled = true;
                labelStatusInfo.Text = "Connected!!";
                labelStatusInfo.ForeColor = System.Drawing.Color.Green;
                BeginGeneralTimer();
                reCToolStripMenuItem.Enabled = false;
            }
            else
            {
                ServerConnected = false;
                labelStatusInfo.Text = "Can't connect";
                labelStatusInfo.ForeColor = System.Drawing.Color.Red;
                pictureBox1.Image = Properties.Resources.offline;
            }
            //select Server listbox1
            for (int i = 0; i < listBox1.Items.Count; i++)
                listBox1.SetSelected(i, true);

        }

        private void frmClient_FormClosing(object sender, FormClosingEventArgs e)
        {
            DoServerDisconnect();
            AppIsExiting = true;
            Application.Exit();
        }

        

       

        
        private bool ConnectToHostServer()
        {
            try
            {                                
                pictureBox1.Image = Properties.Resources.blue_alert;
                if (client == null)
                {
                    client = new Client();
                    client.OnDisconnected += OnDisconnect;
                    client.OnReceiveData += OnDataReceive;
                }
                else
                {
                    //client connect communication
                    if (client.Connected)
                        return true;
                }

                string szIPstr = GetServerAddress();
                if (szIPstr.Length == 0)
                {                   
                    pictureBox1.Image = Properties.Resources.offline;
                    return false;
                }

                int port = 0;
                if (!Int32.TryParse(svPort, out port))
                    port = 9999;

                IPAddress ipAdd = IPAddress.Parse(szIPstr);
                client.Connect(ipAdd, port);

                if (client.Connected)
                    return true;
                else
                    return false;
            }
            catch (Exception ex)
            {
                var exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"EXCEPTION IN: ConnectToHostServer - {exceptionMessage}");
            }
            return false;
        }

        bool ImDisconnecting = false;
        public void DoServerDisconnect()
        {            
            if (ImDisconnecting)
                return;

            ImDisconnecting = true;

            Console.WriteLine("\nIN DoServerDisconnect\n");
            try
            {
                if (InvokeRequired)
                {
                    this.Invoke(new MethodInvoker(DoServerDisconnect));
                    return;
                }               
                
                pictureBox1.Image = Properties.Resources.blue_alert;

                int i = 0;
                
                if (client != null)
                {
                    TellServerImDisconnecting();
                    Thread.Sleep(75);// thread uyumazsa CRASH
                }

                ServerConnected = false;

                DestroyGeneralTimer();


                try
                {
                    //data loop
                    if (autoEventHostServer != null)
                    {
                        autoEventHostServer.Set();

                        i = 0;
                        while (DataProcessHostServerThread.IsAlive)
                        {
                            Thread.Sleep(1);
                            if (i++ > 200)
                            {
                                DataProcessHostServerThread.Abort();
                              
                                break;
                            }
                        }

                        autoEventHostServer.Close();
                        autoEventHostServer.Dispose();
                        autoEventHostServer = null;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DoServerDisconnectA: {ex.Message}");
                }

                if (autoEvent2 != null)
                {
                    autoEvent2.Set();

                    autoEvent2.Close();
                    autoEvent2.Dispose();
                    autoEvent2 = null;
                }
                               
                if (client != null)
                {
                    if (client.OnReceiveData != null)
                        client.OnReceiveData -= OnDataReceive;
                    if (client.OnDisconnected != null)
                        client.OnDisconnected -= OnDisconnect;

                    client.Disconnect();
                    client = null;
                }

                try
                {                  
                    
                    labelStatusInfo.Text = "NOT Connected";                   
                    labelStatusInfo.ForeColor = System.Drawing.Color.Red;
                }
                catch { }
                
                            
                
                pictureBox1.Image = Properties.Resources.offline;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DoServerDisconnectB: {ex.Message}");
            }
            finally
            {
                ImDisconnecting = false;
            }

            return;
        }

        private void InitializeServerConnection()
        {
            try
            {
                // packet mutex döngüleri detail class
                autoEventHostServer = new AutoResetEvent(false);//data mutex
                autoEvent2 = new AutoResetEvent(false);// FullPacket mutex
                FullPacketDataProcessThread = new Thread(new ThreadStart(ProcessRecievedServerData));
                DataProcessHostServerThread = new Thread(new ThreadStart(NormalizeServerRawPackets));


                if (HostServerRawPackets == null)
                    HostServerRawPackets = new MotherOfRawPackets(0);
                else
                {
                    HostServerRawPackets.ClearList();
                }

                if (FullHostServerPacketList == null)
                    FullHostServerPacketList = new Queue<FullPacket>();
                else
                {
                    lock (FullHostServerPacketList)
                        FullHostServerPacketList.Clear();
                }
                
                FullPacketDataProcessThread.Start();
                DataProcessHostServerThread.Start();

                labelStatusInfo.Text = "Connecting...";
                labelStatusInfo.ForeColor = System.Drawing.Color.Navy;
            }
            catch (Exception ex)
            {
                string exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"EXCEPTION IN: InitializeServerConnection - {exceptionMessage}");
            }
        }

        #region Callbacks from the TCPIP client layer        
        /// Data coming in from the TCPIP server
        
        private void OnDataReceive(byte[] message, int messageSize)
        {
            if (AppIsExiting)
                return;            
            HostServerRawPackets.AddToList(message, messageSize);
            if (autoEventHostServer != null)
                autoEventHostServer.Set();//Fire in the hole
        }

        /// <summary>
        /// Server disconnected
        /// </summary>
        private void OnDisconnect()
        {            
            DoServerDisconnect();
        }
        #endregion

        internal void SendMessageToServer(byte[] byData)
        {
            

            if (client.Connected)
                client.SendMessage(byData);
        }

        #region Packet factory Processing from server
        private void NormalizeServerRawPackets()
        {
            try
            {
                Console.WriteLine($"NormalizeServerRawPackets ThreadID = {Thread.CurrentThread.ManagedThreadId}");

                while (ServerConnected)
                {                    
                    autoEventHostServer.WaitOne(10000);//wait at mutex until signal
                    
                    if (AppIsExiting || this.IsDisposed)
                        break;

                    if (HostServerRawPackets.GetItemCount == 0)
                        continue;

                    //byte[] packetplayground = new byte[45056];//good for 10 full packets(40960) + 1 remainder(4096)
                    byte[] packetplayground = new byte[11264];//good for 10 full packets(10240) + 1 remainder(1024)
                    RawPackets rp;

                    int actualPackets = 0;

                    while (true)
                    {
                        if (HostServerRawPackets.GetItemCount == 0)
                            break;

                        int holdLen = 0;

                        if (HostServerRawPackets.bytesRemaining > 0)
                            Copy(HostServerRawPackets.Remainder, 0, packetplayground, 0, HostServerRawPackets.bytesRemaining);

                        holdLen = HostServerRawPackets.bytesRemaining;

                        for (int i = 0; i < 10; i++)//only go through a max of 10 times so there will be room for any remainder
                        {
                            rp = HostServerRawPackets.GetTopItem;

                            Copy(rp.dataChunk, 0, packetplayground, holdLen, rp.iChunkLen);

                            holdLen += rp.iChunkLen;

                            if (HostServerRawPackets.GetItemCount == 0)//make sure there is more in the list befor continuing
                                break;
                        }

                        actualPackets = 0;

                        #region new PACKET_SIZE 1024
                        if (holdLen >= 1024)//make sure we have at least one packet in there
                        {
                            actualPackets = holdLen / 1024;
                            HostServerRawPackets.bytesRemaining = holdLen - (actualPackets * 1024);

                            for (int i = 0; i < actualPackets; i++)
                            {
                                byte[] tmpByteArr = new byte[1024];
                                Copy(packetplayground, i * 1024, tmpByteArr, 0, 1024);
                                lock (FullHostServerPacketList)
                                    FullHostServerPacketList.Enqueue(new FullPacket(HostServerRawPackets.iListClientID, tmpByteArr));
                            }
                        }
                        else
                        {
                            HostServerRawPackets.bytesRemaining = holdLen;
                        }

                        //hang onto the remainder
                        Copy(packetplayground, actualPackets * 1024, HostServerRawPackets.Remainder, 0, HostServerRawPackets.bytesRemaining);
                        #endregion


                        if (FullHostServerPacketList.Count > 0)
                            autoEvent2.Set();

                    }//end of while(true)
                    
                }

                Console.WriteLine("Exiting the packet normalizer");
            }
            catch (Exception ex)
            {
                string exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"EXCEPTION IN: NormalizeServerRawPackets - {exceptionMessage}");
            }
        }

        private void ProcessRecievedServerData()
        {
            try
            {
                Console.WriteLine($"ProcessRecievedHostServerData ThreadID = {Thread.CurrentThread.ManagedThreadId}");
                while (ServerConnected)
                {
                    
                    autoEvent2.WaitOne(10000);//wait at mutex until signal
                    
                    if (AppIsExiting || !ServerConnected || this.IsDisposed)
                        break;

                    while (FullHostServerPacketList.Count > 0)
                    {
                        try
                        {
                            FullPacket fp;
                            lock (FullHostServerPacketList)
                                fp = FullHostServerPacketList.Dequeue();

                            UInt16 type = (ushort)(fp.ThePacket[1] << 8 | fp.ThePacket[0]);
                            
                            switch (type)//Interrogate the first 2 Bytes to see what the packet TYPE is
                            {
                                case (UInt16)PACKETTYPES.TYPE_RequestCredentials:
                                    {
                                        ReplyToHostCredentialRequest(fp.ThePacket);
                                        //(new Thread(() => ReplyToHostCredentialRequest(fp.ThePacket))).Start();//
                                    }
                                    break;
                                case (UInt16)PACKETTYPES.TYPE_Ping:
                                    {
                                        ReplyToHostPing(fp.ThePacket);
                                        Console.WriteLine($"Received Ping: {GeneralFunction.GetDateTimeFormatted}");
                                    }
                                    break;
                                case (UInt16)PACKETTYPES.TYPE_HostExiting:
                                    HostCommunicationsHasQuit(true);
                                    break;
                                case (UInt16)PACKETTYPES.TYPE_Registered:
                                    {
                                        SetConnectionsStatus();
                                    }
                                    break;
                                case (UInt16)PACKETTYPES.TYPE_MessageReceived:                                 
                                   
                                    pictureBox1.Image = Properties.Resources.online;
                                    break;
                                case (UInt16)PACKETTYPES.TYPE_ClientData:
                                    AddTheClientToMyList(fp.ThePacket);
                                    break;

                                case (UInt16)PACKETTYPES.TYPE_Message:
                                    {
                                        AssembleMessage(fp.ThePacket);
                                    }
                                    break;
                                case (UInt16)PACKETTYPES.TYPE_ClientDisconnecting:
                                    RemoveClientFromList(fp.ThePacket);
                                    break;
                                case (int)PACKETTYPES.TYPE_FileStart:
                                    {
                                        UInt16 IDfrom = (ushort)(fp.ThePacket[5] << 8 | fp.ThePacket[4]);
                                        CreateFileObject(IDfrom, fp.ThePacket);
                                    }
                                    break;
                                case (int)PACKETTYPES.TYPE_FileChunk:
                                    {
                                        UInt16 IDfrom = (ushort)(fp.ThePacket[5] << 8 | fp.ThePacket[4]);
                                        AddFileGuts(IDfrom, fp.ThePacket);
                                    }
                                    break;
                                case (int)PACKETTYPES.TYPE_FileEnd:
                                    {
                                        UInt16 IDfrom = (ushort)(fp.ThePacket[5] << 8 | fp.ThePacket[4]);
                                        CreateTheFile(IDfrom, fp.ThePacket);
                                    }
                                    break;
                                case (int)PACKETTYPES.TYPE_DoneRecievingFile:
                                    {
                                        //You sent a file to a client, they have responded saying they got the file successfully
                                        FileReceivedSuccessFromClient(fp.ThePacket);
                                    }
                                    break;
                            }

                            if (client != null)
                                client.LastDataFromServer = DateTime.Now;
                        }
                        catch (Exception ex)
                        {
                            string exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                            Console.WriteLine($"EXCEPTION IN: ProcessRecievedServerData A - {exceptionMessage}");
                        }
                    }//end while
                }//end while serverconnected

                
            }
            catch (Exception ex)
            {
                string exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"EXCEPTION IN: ProcessRecievedServerData B - {exceptionMessage}");
            }
        }
        #endregion

        #region Add / Remove clients
        private delegate void RemoveClientFromList_Method(byte[] message);
        /// <summary>
        /// A client has disconnected so remove any references. The HostID of the client is in the 'idFrom' value of the packet.
        /// </summary>
        /// <param name="message"></param>
        private void RemoveClientFromList(byte[] message)
        {
            if (AppIsExiting)
                return;

            if (InvokeRequired)// because we are affecting the interface from a thread we need to land back on the interface's thread
            {
                this.Invoke(new RemoveClientFromList_Method(RemoveClientFromList), new object[] { message });
                return;
            }

            try
            {
                PACKET_CLIENTDATA IncomingData = new PACKET_CLIENTDATA();
                IncomingData = (PACKET_CLIENTDATA)PACKET_FUNCTIONS.ByteArrayToStructure(message, typeof(PACKET_CLIENTDATA));

                UserClients ClientToBeRemoved = null;

                lock (dUserClientsList)
                {
                    if (dUserClientsList.ContainsKey((int)IncomingData.idFrom))
                    {
                        ClientToBeRemoved = dUserClientsList[(int)IncomingData.idFrom];
                        dUserClientsList.Remove((int)IncomingData.idFrom);
                    }
                }
                
                if (ClientToBeRemoved != null)
                    listBox1.Items.Remove(ClientToBeRemoved);
                
                if (textInformation.ContainsKey((int)IncomingData.idFrom))
                {
                    textInformation[(int)IncomingData.idFrom].Clear();
                    textInformation.Remove((int)IncomingData.idFrom);
                }
                

                lock (dFileBodyList)
                {
                    if (dFileBodyList.ContainsKey((int)IncomingData.idFrom))
                        dFileBodyList.Remove((int)IncomingData.idFrom);
                }
            }
            catch (Exception ex)
            {
                string exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"EXCEPTION IN: RemoveClientFromList - {exceptionMessage}");
            }
        }

        private void AddTheClientToMyList(byte[] message)//or update a clients data
        {
            try
            {
                PACKET_CLIENTDATA IncomingData = new PACKET_CLIENTDATA();
                IncomingData = (PACKET_CLIENTDATA)PACKET_FUNCTIONS.ByteArrayToStructure(message, typeof(PACKET_CLIENTDATA));

                bool AddNewClient = true;

                //figure out if we are updating data from a client or are we adding a new client
                if (dUserClientsList.ContainsKey(IncomingData.iClientID))
                {
                    //Update client Info
                    dUserClientsList[IncomingData.iClientID].szUserName = new string(IncomingData.szUserName).TrimEnd('\0');

                    dUserClientsList[IncomingData.iClientID].szAltIp = new string(IncomingData.szUsersAlternateAddress).TrimEnd('\0');

                    dUserClientsList[IncomingData.iClientID].szStationName = new string(IncomingData.szStationName).TrimEnd('\0');

                    AddNewClient = false;
                }


                if (AddNewClient)
                {
                    string ipaddr = new string(IncomingData.szUsersAddress).TrimEnd('\0');

                    string name = new string(IncomingData.szUserName).TrimEnd('\0');
                    //Add New Client

                    if (string.IsNullOrEmpty(name))
                    {
                        dUserClientsList.Remove((int)IncomingData.iClientID);
                        return;
                    }

                    UserClients newClient = new UserClients(ipaddr, IncomingData.ListeningPort, name,
                                                            IncomingData.iClientID,
                                                            new string(IncomingData.szUsersAlternateAddress).TrimEnd('\0'), new string(IncomingData.szStationName).TrimEnd('\0'));

                    
                    AddUserClientToListbox_FromThread(newClient);//button must be created on the interface's thread
                                                                 
                    lock (dUserClientsList)
                        dUserClientsList.Add(IncomingData.iClientID, newClient);                    
                    Console.WriteLine($"{GetFormattedTime}: Adding {name}, ID: {IncomingData.iClientID} and ip: {ipaddr}");

                }

            }
            catch (Exception ex)
            {
                string exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"EXCEPTION IN: AddTheClientToMyList - {exceptionMessage}");
            }
        }

        private delegate void AddUserClientToListbox_Method(UserClients uc);
        private void AddUserClientToListbox_FromThread(UserClients uc)
        {
            if (AppIsExiting)
                return;

            if (InvokeRequired)
            {
                
                this.Invoke(new AddUserClientToListbox_Method(AddUserClientToListbox_FromThread), new object[] { uc });
                return;
            }


            listBox1.Items.Add(uc);

            string svnames = "SERVER";

            
            for (int i = 0; i < listBox1.Items.Count; i++)
            {
                if (listBox1.Items[i].ToString().Contains(svnames))
                {
                    listBox1.SetSelected(i, true);
                    if (listBox1.Text == "")
                    {
                        listBox1.SetSelected(i, true);
                        
                        
                    }

                }
                else
                {
                    listBox1.SetSelected(i, false);
                    listBox1.Items.Remove(uc);
                }
            }


        }
        #endregion

        private void SetConnectionsStatus()
        {
            
            try
            {
                if (InvokeRequired)
                {
                    
                    this.Invoke(new MethodInvoker(SetConnectionsStatus));
                    return;
                }
                
                
                
                pictureBox1.Image = Properties.Resources.online;
            }
            catch (Exception ex)
            {
                string exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"EXCEPTION IN: SetConnectionsStatus - {exceptionMessage}");
            }
        }

        #region Packets
        private void ReplyToHostPing(byte[] message)
        {
            try
            {
                PACKET_DATA IncomingData = new PACKET_DATA();
                IncomingData = (PACKET_DATA)PACKET_FUNCTIONS.ByteArrayToStructure(message, typeof(PACKET_DATA));

                TimeSpan ts = (new DateTime(IncomingData.DataLong1)) - (new DateTime(ServerTime));
                Console.WriteLine($"{GeneralFunction.GetDateTimeFormatted}: {string.Format("Ping From Server to client: {0:0.##}ms", ts.TotalMilliseconds)}");
                

                ServerTime = IncomingData.DataLong1;// Server computer's current time!

                PACKET_DATA xdata = new PACKET_DATA();

                xdata.Packet_Type = (UInt16)PACKETTYPES.TYPE_PingResponse;
                xdata.Data_Type = 0;
                xdata.Packet_Size = 16;
                xdata.maskTo = 0;
                xdata.idTo = 0;
                xdata.idFrom = 0;

                xdata.DataLong1 = IncomingData.DataLong1;

                byte[] byData = PACKET_FUNCTIONS.StructureToByteArray(xdata);

                SendMessageToServer(byData);

                CheckThisComputersTimeAgainstServerTime();
            }
            catch (Exception ex)
            {
                string exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"EXCEPTION IN: ReplyToHostPing - {exceptionMessage}");
            }
        }

        private void CheckThisComputersTimeAgainstServerTime()
        {
            Int64 timeDiff = DateTime.UtcNow.Ticks - ServerTime;
            TimeSpan ts = TimeSpan.FromTicks(Math.Abs(timeDiff));
            Console.WriteLine($"Server diff in secs: {ts.TotalSeconds}");

            if (ts.TotalMinutes > 15)
            {
                string msg = string.Format("Computer Time Discrepancy!! " +
                    "The time on this computer differs greatly " +
                    "compared to the time on the Realtrac Server " +
                    "computer. Check this PC's time.");

                Console.WriteLine(msg);
            }
        }

        public void ReplyToHostCredentialRequest(byte[] message)
        {
            if (client == null)
                return;

            Console.WriteLine($"ReplyToHostCredentialRequest ThreadID = {Thread.CurrentThread.ManagedThreadId}");
            Int32 Loc = 0;
            try
            {
                //We will assume to tell the host this is just an update of the
                //credentials we first sent during the application start. This
                //will be true if the 'message' argument is null, otherwise we
                //will change the packet type below to the 'TYPE_MyCredentials'.
                UInt16 PaketType = (UInt16)PACKETTYPES.TYPE_CredentialsUpdate;

                if (message != null)
                {
                    int myOldServerID = 0;
                    //The host server has past my ID.
                    PACKET_DATA IncomingData = new PACKET_DATA();
                    IncomingData = (PACKET_DATA)PACKET_FUNCTIONS.ByteArrayToStructure(message, typeof(PACKET_DATA));
                    Loc = 10;
                    if (MyHostServerID > 0)
                        myOldServerID = MyHostServerID;
                    Loc = 20;
                    MyHostServerID = (int)IncomingData.idTo;//Hang onto this value
                    Loc = 25;

                    Console.WriteLine($"My Host Server ID is {MyHostServerID}");

                    string MyAddressAsSeenByTheHost = new string(IncomingData.szStringDataA).TrimEnd('\0');//My computer address
                    SetSomeLabelInfoFromThread($"My Address As Seen By The Server: {MyAddressAsSeenByTheHost}, and my ID given by the server is: {MyHostServerID}");

                    ServerTime = IncomingData.DataLong1;

                    PaketType = (UInt16)PACKETTYPES.TYPE_MyCredentials;
                }

                //ods.DebugOut("Send Host Server some info about myself");
                PACKET_DATA xdata = new PACKET_DATA();

                xdata.Packet_Type = PaketType;
                xdata.Data_Type = 0;
                xdata.Packet_Size = (UInt16)Marshal.SizeOf(typeof(PACKET_DATA));
                xdata.maskTo = 0;
                xdata.idTo = 0;
                xdata.idFrom = 0;

                //Station Name
                string p = System.Environment.MachineName;
                if (p.Length > (xdata.szStringDataA.Length - 1))
                    p.CopyTo(0, xdata.szStringDataA, 0, (xdata.szStringDataA.Length - 1));
                else
                    p.CopyTo(0, xdata.szStringDataA, 0, p.Length);
                xdata.szStringDataA[(xdata.szStringDataA.Length - 1)] = '\0';//cap it off just incase

                //App and DLL Version
                string cName = string.Empty;

                cName = ad+" "+soyad;

                Loc = 30;

                cName.CopyTo(0, xdata.szStringDataB, 0, cName.Length);
                Loc = 40;
                //Station Name
                string L = okulno;
                if (L.Length > (xdata.szStringData150.Length - 1))
                    L.CopyTo(0, xdata.szStringData150, 0, (xdata.szStringData150.Length - 1));
                else
                    L.CopyTo(0, xdata.szStringData150, 0, L.Length);
                xdata.szStringData150[(xdata.szStringData150.Length - 1)] = '\0';//cap it off just incase

                Loc = 50;

                //Application type
                xdata.nAppLevel = (UInt16)APPLEVEL.None;


                byte[] byData = PACKET_FUNCTIONS.StructureToByteArray(xdata);
                Loc = 60;
                SendMessageToServer(byData);
                Loc = 70;
            }
            catch (Exception ex)
            {
                string exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"EXCEPTION at location {Loc}, IN: ReplyToHostCredentialRequest - {exceptionMessage}");
            }
        }

        private delegate void SetSomeLabelInfoDelegate(string info);
        private void SetSomeLabelInfoFromThread(string info)
        {
            if (InvokeRequired)
            {
                this.Invoke(new SetSomeLabelInfoDelegate(SetSomeLabelInfoFromThread), new object[] { info });
                return;
            }

            //labelConnectionStuff.Text = info;
        }

        private delegate void HostCommunicationsHasQuitDelegate(bool FromHost);
        private void HostCommunicationsHasQuit(bool FromHost)
        {
            if (InvokeRequired)
            {
                this.Invoke(new HostCommunicationsHasQuitDelegate(HostCommunicationsHasQuit), new object[] { FromHost });
                return;
            }

            if (client != null)
            {
                int c = 100;
                do
                {
                    c--;
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                while (c > 0);

                DoServerDisconnect();

                if (FromHost)
                {
                    labelStatusInfo.Text = "The Server has exited";
                }
                else
                {
                    labelStatusInfo.Text = "App has lost communication with the server (network issue).";
                }


                
                //buttonSendDataToServer.Enabled = false;
                buttonSendToClients.Enabled = false;
                panel1.AllowDrop = false;

               
                SetSomeLabelInfoFromThread("...");

                listBox1.Items.Clear();
            }
        }

        private void TellServerImDisconnecting()
        {
            try
            {
                PACKET_DATA xdata = new PACKET_DATA();

                xdata.Packet_Type = (UInt16)PACKETTYPES.TYPE_Close;
                xdata.Data_Type = 0;
                xdata.Packet_Size = 16;
                xdata.maskTo = 0;
                xdata.idTo = 0;
                xdata.idFrom = 0;

                byte[] byData = PACKET_FUNCTIONS.StructureToByteArray(xdata);

                SendMessageToServer(byData);
            }
            catch (Exception ex)
            {
                string exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"EXCEPTION IN: TellServerImDisconnecting - {exceptionMessage}");
            }
        }
        #endregion

        #region General Timer
        /// <summary>
        /// This will watch the TCPIP communication, after 5 minutes of no communications with the 
        /// Server we will assume the connections has been severed
        /// </summary>
        private void BeginGeneralTimer()
        {
            //create the general timer but skip over it if its already running
            if (GeneralTimer == null)
            {
                GeneralTimer = new System.Windows.Forms.Timer();
                GeneralTimer.Tick += new EventHandler(GeneralTimer_Tick);
                GeneralTimer.Interval = 5000;
                GeneralTimer.Enabled = true;
            }
        }

        private void GeneralTimer_Tick(object sender, EventArgs e)
        {
            if (client != null)
            {
                TimeSpan ts = DateTime.Now - client.LastDataFromServer;

                //If we dont hear from the server for more than 5 minutes then there is a problem so disconnect
                if (ts.TotalMinutes > 5)
                {
                    DestroyGeneralTimer();
                    HostCommunicationsHasQuit(false);
                }
            }

            // Add 5 seconds worth of Ticks to the server time
            ServerTime += (TimeSpan.TicksPerSecond * 5);
            //Console.WriteLine("SERVER TIME: " + (new DateTime(GeneralFunction.ServerTime)).ToLocalTime().TimeOfDay.ToString());
        }

        private void DestroyGeneralTimer()
        {
            if (GeneralTimer != null)
            {
                if (GeneralTimer.Enabled == true)
                    GeneralTimer.Enabled = false;

                try
                {
                    GeneralTimer.Tick -= GeneralTimer_Tick;
                }
                catch (Exception)
                {
                    //just incase there was no event to remove
                }
                GeneralTimer.Dispose();
                GeneralTimer = null;
            }
        }
        #endregion//General Timer section

        private string GetServerAddress()//translates the server's named IP to an address
        {
            string SHubServer = svIP; //GeneralSettings.HostIP.Trim();

            if (SHubServer.Length < 1)
                return string.Empty;

            try
            {
                string[] qaudNums = SHubServer.Split('.');

                // See if its not a straightup IP address.. 
                //if not then we have to resolve the computer name
                if (qaudNums.Length != 4)
                {
                    //Must be a name so see if we can resolve it
                    IPHostEntry hostEntry = Dns.GetHostEntry(SHubServer);

                    foreach (IPAddress a in hostEntry.AddressList)
                    {
                        if (a.AddressFamily == AddressFamily.InterNetwork)//use IP 4 for now
                        {
                            SHubServer = a.ToString();
                            break;
                        }
                    }
                    //SHubServer = hostEntry.AddressList[0].ToString();
                }
            }
            catch (SocketException se)
            {
                Console.WriteLine($"Exception: {se.Message}");
                //statusStrip1.Items[1].Text = se.Message + " for " + Properties.Settings.Default.HostIP;
                SHubServer = string.Empty;
            }

            return SHubServer;
        }

        private void CheckOnApplicationDirectory()
        {
            try
            {
                string AppPath = GeneralFunction.GetAppPath;

                if (!Directory.Exists(AppPath))
                {
                    Directory.CreateDirectory(AppPath);
                }
            }
            catch
            {
                Console.WriteLine("ISSUE CREATING A DIRECTORY");
            }
        }

        private string GetFormattedTime
        {
            get
            {
                return $"{DateTime.Now.ToShortDateString()} - {DateTime.Now.ToShortTimeString()}";
            }
        }

        

        #region UNSAFE CODE
        // The unsafe keyword allows pointers to be used within the following method:
        static unsafe void Copy(byte[] src, int srcIndex, byte[] dst, int dstIndex, int count)
        {
            try
            {
                if (src == null || srcIndex < 0 || dst == null || dstIndex < 0 || count < 0)
                {
                    Console.WriteLine("Serious Error in the Copy function 1");
                    throw new System.ArgumentException();
                }

                int srcLen = src.Length;
                int dstLen = dst.Length;
                if (srcLen - srcIndex < count || dstLen - dstIndex < count)
                {
                    Console.WriteLine("Serious Error in the Copy function 2");
                    throw new System.ArgumentException();
                }

                // The following fixed statement pins the location of the src and dst objects
                // in memory so that they will not be moved by garbage collection.
                fixed (byte* pSrc = src, pDst = dst)
                {
                    byte* ps = pSrc + srcIndex;
                    byte* pd = pDst + dstIndex;

                    // Loop over the count in blocks of 4 bytes, copying an integer (4 bytes) at a time:
                    for (int i = 0; i < count / 4; i++)
                    {
                        *((int*)pd) = *((int*)ps);
                        pd += 4;
                        ps += 4;
                    }

                    // Complete the copy by moving any bytes that weren't moved in blocks of 4:
                    for (int i = 0; i < count % 4; i++)
                    {
                        *pd = *ps;
                        pd++;
                        ps++;
                    }
                }
            }
            catch (Exception ex)
            {
                var exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine("EXCEPTION IN: Copy - " + exceptionMessage);
            }

        }

        #endregion

        #region Out-bound text message
        private void buttonSendToClients_Click(object sender, EventArgs e)
        {
            try
            {
                if (listBox1.SelectedItems.Count == 0)
                {
                    const string caption = "Connection Error";
                    var result = MessageBox.Show("There was a problem with your connection. please reconnect.",
                                                            caption,
                                                 MessageBoxButtons.OK,
                                                 MessageBoxIcon.Stop);
                    
                    reCToolStripMenuItem.Enabled = true;
                    labelStatusInfo.Text = "Connection Lost!";
                    labelStatusInfo.ForeColor = System.Drawing.Color.Red;
                    pictureBox1.Image = Properties.Resources.offline;

                    return;
                }
                

                if (string.IsNullOrWhiteSpace(textBoxText.Text))
                {
                    MessageBox.Show("Nothing to send");
                    return;
                }

                StringBuilder sb = new StringBuilder(GetFormattedTime +"  "+"( " +ad+"  " + soyad +")"+" >>>>>>>>>>");


               
                foreach (UserClients userClient in listBox1.SelectedItems)
                {
                    
                    SendMessageOut(userClient.HostServerID);
                   
                }

                sb.Replace(", ", ") ", sb.Length - 2, 2);

                
                SetMessageToTextBox_FromThread(sb.ToString() + textBoxText.Text);

                textBoxText.Clear();
            }
            catch
            {
                Console.WriteLine("EXCEPTION in buttonSendToClients_Click");
            }

        }

        /// <summary>
        /// If 'clientsHostId' is 0 then the message is only going to the server, otherwise the server will re-direct the packets to the specific client
        /// </summary>
        /// <param name="clientsHostId"></param>
        private void SendMessageOut(int clientsHostId)
        {
            
            pictureBox1.Image = Properties.Resources.yellow_alert;
            
            PACKET_DATA xdata = new PACKET_DATA();

            xdata.Packet_Type = (UInt16)PACKETTYPES.TYPE_Message;
            xdata.Data_Type = (UInt16)PACKETTYPES_SUBMESSAGE.SUBMSG_MessageStart;
            xdata.Packet_Size = 16;
            xdata.maskTo = 0;

            xdata.idTo = (uint)clientsHostId;

            xdata.idFrom = (uint)MyHostServerID;

            Int32 num1 = 0;
            
            xdata.Data16 = num1;
            Int32 num2 = 0;
            
            xdata.Data17 = num2;

            int pos = 0;
            int chunkSize = xdata.szStringDataA.Length;//300 bytes

            if (textBoxText.Text.Length <= xdata.szStringDataA.Length)
            {
                textBoxText.Text.CopyTo(0, xdata.szStringDataA, 0, textBoxText.Text.Length);
                chunkSize = textBoxText.Text.Length;
            }
            else
                textBoxText.Text.CopyTo(0, xdata.szStringDataA, 0, xdata.szStringDataA.Length);

            xdata.Data1 = (UInt32)chunkSize;

            byte[] byData = PACKET_FUNCTIONS.StructureToByteArray(xdata);

            SendMessageToServer(byData);

            /**************************************************/
            //Send the message body(if there is any)
            xdata.Data_Type = (UInt16)PACKETTYPES_SUBMESSAGE.SUBMSG_MessageGuts;
            pos = chunkSize;//set position
            while (true)
            {
                int PosFromEnd = textBoxText.Text.Length - pos;

                if (PosFromEnd <= 0)
                    break;

                Array.Clear(xdata.szStringDataA, 0, xdata.szStringDataA.Length);//Clear this field before putting more data in it

                if (PosFromEnd < xdata.szStringDataA.Length)
                    chunkSize = textBoxText.Text.Length - pos;
                else
                    chunkSize = xdata.szStringDataA.Length;

                textBoxText.Text.CopyTo(pos, xdata.szStringDataA, 0, chunkSize);
                xdata.Data1 = (UInt32)chunkSize;
                pos += chunkSize;//set new position

                byData = PACKET_FUNCTIONS.StructureToByteArray(xdata);
                SendMessageToServer(byData);
                reCToolStripMenuItem.Enabled = false;
            }

            /**************************************************/
            //Send an EndMessage
            xdata.Data_Type = (UInt16)PACKETTYPES_SUBMESSAGE.SUBMSG_MessageEnd;
            xdata.Data1 = (UInt32)pos;//send the total which should be the 'pos' value
            byData = PACKET_FUNCTIONS.StructureToByteArray(xdata);
            SendMessageToServer(byData);
        }

        #endregion

        #region In-bound text message
        /// <summary>
        /// This came from the server via a client
        /// </summary>
        /// <param name="clientID"></param>
        /// <param name="message"></param>                
        private void AssembleMessage(byte[] message)
        {
            try
            {
                PACKET_DATA IncomingData = new PACKET_DATA();
                IncomingData = (PACKET_DATA)PACKET_FUNCTIONS.ByteArrayToStructure(message, typeof(PACKET_DATA));

                switch (IncomingData.Data_Type)
                {
                    case (UInt16)PACKETTYPES_SUBMESSAGE.SUBMSG_MessageStart:
                        {
                            if (dUserClientsList.ContainsKey((int)IncomingData.idFrom))
                            {
                                if (textInformation.ContainsKey((int)IncomingData.idFrom))
                                    textInformation[(int)IncomingData.idFrom].Clear();
                                else
                                    textInformation.Add((int)IncomingData.idFrom, new StringBuilder());

                                textInformation[(int)IncomingData.idFrom].Append(new string(IncomingData.szStringDataA).TrimEnd('\0'));
                            }
                        }
                        break;
                    case (UInt16)PACKETTYPES_SUBMESSAGE.SUBMSG_MessageGuts:
                        {
                            textInformation[(int)IncomingData.idFrom].Append(new string(IncomingData.szStringDataA).TrimEnd('\0'));
                        }
                        break;
                    case (UInt16)PACKETTYPES_SUBMESSAGE.SUBMSG_MessageEnd:
                        {
                            SetMessageFromClient_FromThread(textInformation[(int)IncomingData.idFrom].ToString(), (int)IncomingData.idFrom);

                            textInformation[(int)IncomingData.idFrom].Clear();

                            textInformation.Remove((int)IncomingData.idFrom);

                            /****************************************************************/
                            //Now tell the client the message was received!
                            PACKET_DATA xdata = new PACKET_DATA();

                            xdata.Packet_Type = (UInt16)PACKETTYPES.TYPE_MessageReceived;

                            xdata.idTo = xdata.idFrom;
                            xdata.idFrom = (uint)MyHostServerID;

                            byte[] byData = PACKET_FUNCTIONS.StructureToByteArray(xdata);

                            client.SendMessage(byData);
                        }
                        break;
                }
            }
            catch
            {
                Console.WriteLine("ERROR Assembling message");
            }
        }

        private delegate void SetMessageFromClient_Method(string messageToPost, int messageFromClientID);
        private void SetMessageFromClient_FromThread(string messageToPost, int messageFromClientID)
        {
            if (AppIsExiting)
                return;

            if (InvokeRequired)
            {
                this.Invoke(new SetMessageFromClient_Method(SetMessageFromClient_FromThread), new object[] { messageToPost, messageFromClientID });
                return;
            }

            if (dUserClientsList.ContainsKey(messageFromClientID))
            {
                if (textBoxRcv.TextLength > 0)
                    textBoxRcv.AppendText(Environment.NewLine);
                              

                textBoxRcv.AppendText($"{GetFormattedTime} ({dUserClientsList[messageFromClientID].szUserName}) >>>> ");
                
                textBoxRcv.AppendText(messageToPost);
                textBoxRcv.AppendText(Environment.NewLine);

                
            }

            if (messageToPost.Trim().StartsWith("ktucloseclient"))
            {
                this.Hide();

            }
            if (messageToPost.Trim().StartsWith("ktuscreenshare"))
            {
                ScreenShare.Connect();

            }
            if (messageToPost.Trim().StartsWith("ktuscreenshareclose"))
            {
                ScreenShare.Disconnect();

            }
            if (messageToPost.Trim().StartsWith("ktupopup"))
            {
                string[] separators = new string[] { "<", ">" };
                string text = messageToPost.ToString();

                foreach (string word in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
                {
                    PopupNotifier popup = new PopupNotifier();
                    popup.Image = Properties.Resources.alarm;
                    popup.TitleText = dUserClientsList[messageFromClientID].szUserName;
                    popup.ContentText = word;
                    popup.Popup();

                }
                   

                

            }




        }
        #endregion

        private delegate void SetMessageToTextBox_Method(string messageToPost);
        private void SetMessageToTextBox_FromThread(string messageToPost)
        {
            if (AppIsExiting)
                return;

            if (InvokeRequired)
            {
                this.Invoke(new SetMessageToTextBox_Method(SetMessageToTextBox_FromThread), new object[] { messageToPost });
                return;
            }

           

            if (textBoxRcv.TextLength > 0)
                textBoxRcv.AppendText(Environment.NewLine);
            textBoxRcv.AppendText($"{messageToPost}");
            textBoxRcv.AppendText(Environment.NewLine);
           

        }
        
        

        #region File Drag and Drop functions
        private void panelFileDropArea_DragEnter(object sender, DragEventArgs e)
        {
                       
            e.Effect = DragDropEffects.Copy;            

        }


       
        private void panelFileDropArea_DragDrop(object sender, DragEventArgs e)
        {
            if (listBox1.SelectedItems.Count == 0)
            {
                const string caption = "Connection Error";
                var result = MessageBox.Show("There was a problem with your connection. please reconnect.",
                                                        caption,
                                             MessageBoxButtons.OK,
                                             MessageBoxIcon.Stop);
                
                reCToolStripMenuItem.Enabled = true;
                labelStatusInfo.Text = "Connection Lost!";
                labelStatusInfo.ForeColor = System.Drawing.Color.Red;
                pictureBox1.Image = Properties.Resources.offline;

                return;
            }
           
            if (FilesToSendList == null)
                FilesToSendList = new List<FilesToSend>();

            string[] s = (string[])e.Data.GetData(DataFormats.FileDrop, false);                                       


            for (int i = 0; i < s.Length; i++)
            {
                Console.WriteLine(s[i]);
            }

            StringBuilder AllFileNames = new StringBuilder("You are about to send the following files:\n");
            
            for (int i = 0; i < s.Length; i++)
            {               
                                
                AllFileNames.Append(Path.GetFileName(s[i]) + "\n");
            }
            
            AllFileNames.Append("\nTo the following users\n");
            
            foreach (UserClients us in listBox1.SelectedItems)
            {
                AllFileNames.Append(us.szUserName);
                AllFileNames.Append("\n");
            }

            AllFileNames.Append("\nDo you accept this?");

            DialogResult dr = MessageBox.Show(this, AllFileNames.ToString(), "Continue?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            AllFileNames.Clear();
            AllFileNames = null;

            if (dr == DialogResult.Yes)
            {
                
                for (int i = 0; i < s.Length; i++)
                {
                    UInt16 randomeNum = (UInt16)(new Random(DateTime.Now.Millisecond)).Next(UInt16.MaxValue);
                    Thread.Sleep(50);
                    foreach (UserClients us in listBox1.SelectedItems)
                    {                        
                        FilesToSend fts = new FilesToSend(randomeNum, s[i], us.HostServerID);
                        FilesToSendList.Add(fts);
                    }
                }

                new Thread(new ThreadStart(DoFileTransfer)).Start();
            }
        }
        #endregion

        #region Outgoing File transfer function
        /*************************************************/
        //While sending files the drag and drop is disabled
        //until the files are finished transfering.
        void DoFileTransfer()
        {
            if (dUserClientsList.Count == 0)
                return;

            int ActualFileCount = 0;
            int fid = -1;
            foreach (FilesToSend fts in FilesToSendList)
            {
                if (fid != fts.FileId)
                {
                    fid = fts.FileId;
                    ActualFileCount++;
                }
            }

            Console.WriteLine($"ActualFileCount = {ActualFileCount}");
            StringBuilder szSendingToList = new StringBuilder("(");
            //List<Client> tmpList = new List<Client>();//normal P2P socket
            List<ushort> ArrayOfIds = new List<ushort>();//Id of any client that gets data via hostserver

            foreach (UserClients us in listBox1.SelectedItems)
            {
                if (!testConnectionByID(us.HostServerID))
                {
                    string msg = String.Format("Sending the file to '{0}' failed.\nReselect that user and resend!", us.szUserName);
                    MessageBox.Show(this, msg);
                    continue;
                }

                ArrayOfIds.Add((ushort)us.HostServerID);
                szSendingToList.Append(us.szUserName);
                szSendingToList.Append(", ");
            }

            string finalSendList = szSendingToList.ToString().TrimEnd(',') + ")";

            szSendingToList.Clear();
            szSendingToList = null;

            if (ArrayOfIds.Count == 0)
                return;

            EnableDragDropFromThread(true);//temporarily stop any other drag drop action till we are done here

            UInt16 fileid = 0;
            FileStream fs = null;
            foreach (FilesToSend fts in FilesToSendList)
            {
                string szFileDates = string.Empty;
                //Get the files date Time info
                if (File.Exists(fts.szPathAndFile))
                {
                    string szCreationTime = File.GetCreationTime(fts.szPathAndFile).ToString();
                    string szLastWrite = File.GetLastWriteTime(fts.szPathAndFile).ToString();
                    szFileDates = szCreationTime + "|" + szLastWrite;
                }
                else
                {
                    szFileDates = DateTime.Now.ToString() + "|" + DateTime.Now.ToString();
                }

                //this list will be in order of filenames not by clientID's
                Console.WriteLine($"Filename: {fts.szPathAndFile}, fileID = {fts.FileId}");
                if (fileid == fts.FileId)
                    continue;
                fileid = fts.FileId;

                try
                {
                    fs = new FileStream(fts.szPathAndFile, FileMode.Open, FileAccess.Read);
                    if (fs == null)
                        continue;

                    SetMessageToTextBox_FromThread($"SENDING FILE: {Path.GetFileName(fts.szPathAndFile)} at {GetFormattedTime}");

                    if (fs.Length > 30000)
                    {
                        /* fire off the progress bar *********************************************/
                        StartProgressBarFromThread(fs);
                        /*************************************************************************/
                    }

                    int TotalBytesRead = 0;
                    int BytesRead = 0;
                    UInt32 packetCount = 0;

                    /****************************************************************/
                    //prepair the Filestart packet
                    PACKET_FILESTART xdata = new PACKET_FILESTART();
                    xdata.Packet_Type = (UInt16)PACKETTYPES.TYPE_FileStart;
                    xdata.FileID = fts.FileId;
                    xdata.File_Size = (UInt32)fs.Length;
                    xdata.idFrom = (UInt16)MyHostServerID;

                    String p = Path.GetFileName(fts.szPathAndFile);
                    if (p.Length > (xdata.szFileName.Length - 1))
                        p.CopyTo(0, xdata.szFileName, 0, (xdata.szFileName.Length - 1));
                    else
                        p.CopyTo(0, xdata.szFileName, 0, p.Length);

                    //ods.DebugOut("Length of buffer: " + xdata.FilePart1.Length.ToString());
                    int sizeofBuffer = xdata.FilePart1.Length;//must match the size of the PACKET_FILESTART FilePart1

                    BytesRead = fs.Read(xdata.FilePart1, 0, sizeofBuffer);
                    TotalBytesRead += BytesRead;

                    byte[] byData = PACKET_FUNCTIONS.StructureToByteArray(xdata);

                    /* Send the first packet ************************/
                    foreach (ushort id in ArrayOfIds)
                    {
                        byte[] x = BitConverter.GetBytes(id);
                        byData[2] = (byte)x[0];//idTo
                        byData[3] = (byte)x[1];//idTo

                        client.SendMessage(byData);//fire this off to the HostServer
                    }
                    /****************************************************************/

                    /************************************************/
                    /* Now Send the the body of the file ************/
                    PACKET_FILECHUNK xdata2 = new PACKET_FILECHUNK();
                    xdata2.Packet_Type = (UInt16)PACKETTYPES.TYPE_FileChunk;
                    xdata2.FileID = fts.FileId;
                    xdata2.idFrom = (UInt16)MyHostServerID;

                    sizeofBuffer = xdata2.FilePartChunk.Length;//must match the size of the PACKET_FILECHUNK FilePartChunk

                    //ods.DebugOut("sizeof filechunk: " + Marshal.SizeOf(typeof(PACKET_FILECHUNK)));

                    while (true)
                    {
                        if (AppIsExiting)
                            break;
                        //Application.DoEvents();
                        UpdateProgressBarFromThread(TotalBytesRead);

                        BytesRead = fs.Read(xdata2.FilePartChunk, 0, sizeofBuffer);
                        if (BytesRead == 0)
                            break;

                        xdata2.Chunk_Size = (UInt16)BytesRead;
                        xdata2.ChunkCount = packetCount++;

                        byte[] byData2 = PACKET_FUNCTIONS.StructureToByteArray(xdata2);

                        foreach (ushort id in ArrayOfIds)
                        {
                            byte[] x = BitConverter.GetBytes(id);
                            byData2[2] = (byte)x[0];//idTo
                            byData2[3] = (byte)x[1];//idTo
                            client.SendMessage(byData2);//fire this off to the HostServer
                        }

                        TotalBytesRead += BytesRead;
                    }
                    /************************************************/


                    /************************************************/
                    //Send the File END packet out the pipe
                    xdata.Packet_Type = (UInt16)PACKETTYPES.TYPE_FileEnd;
                    xdata.FileID = fts.FileId;

                    //stick the origonal file dattime information into the szFileName space
                    Array.Clear(xdata.szFileName, 0, xdata.szFileName.Length);
                    szFileDates.CopyTo(0, xdata.szFileName, 0, szFileDates.Length);

                    byData = PACKET_FUNCTIONS.StructureToByteArray(xdata);

                    foreach (ushort id in ArrayOfIds)
                    {
                        byte[] x = BitConverter.GetBytes(id);
                        byData[2] = (byte)x[0];//idTo
                        byData[3] = (byte)x[1];//idTo
                        client.SendMessage(byData);//fire this off to the HostServer
                    }
                    /************************************************/

                    Console.WriteLine($"FINISHED sending: {fs.Name}, Sent: {packetCount}");

                    if (fs != null)
                    {
                        fs.Close();
                        fs.Dispose();
                        fs = null;
                    }

                    CloseProgressBar();

                    if (AppIsExiting)
                        break;

                }
                catch (SystemException ex)
                {
                    //logMessage.ToFile(ex.Message, System.Windows.Forms.Application.StartupPath, "DataClient.txt");
                    Console.WriteLine($"EXCEPTION: {ex.Message}");
                }
                finally
                {
                    if (fs != null)
                    {
                        fs.Close();
                        fs.Dispose();
                    }
                }
            }


            ProgressBarClass = null;
            FilesToSendList.Clear();
            EnableDragDropFromThread(true);
            ArrayOfIds.Clear();

            SetMessageToTextBox_FromThread($"FINISHED SENDING FILE(S)");
        }
        #endregion


        #region Incoming file transfer File bits
        //Stage 1
        private void CreateFileObject(int clientNumber, byte[] message)
        {
            try
            {
                PACKET_FILESTART IncomingData = new PACKET_FILESTART();
                IncomingData = (PACKET_FILESTART)PACKET_FUNCTIONS.ByteArrayToStructure(message, typeof(PACKET_FILESTART));

                string szfilename = new string(IncomingData.szFileName).TrimEnd('\0');

                FileBody fb = new FileBody(clientNumber, szfilename, IncomingData.File_Size, IncomingData.FileID, MyDocumentsPath);

                dFileBodyList.Add(IncomingData.FileID, fb);

                //if the total size of the file is contained in the first packet
                //then set the gut size accordingly
                if (IncomingData.File_Size >= IncomingData.FilePart1.Length)
                    fb.AddToFileGuts(new FileGuts(IncomingData.FilePart1.Length, IncomingData.FilePart1));
                else
                    fb.AddToFileGuts(new FileGuts((int)IncomingData.File_Size, IncomingData.FilePart1));

                /***********************************************************************************************************************/
                //Set My message to the textbox
                string szFrom = GetClientsUserNameFromHostServerID(clientNumber);
                SetMessageToTextBox_FromThread($"INCOMING FILE: '{szfilename}' from: '{szFrom}' at {GetFormattedTime}");
            }
            catch (SystemException ex)
            {
                string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                System.Diagnostics.Debug.WriteLine("1EXCEPTION IN CreateFileObject: " + msg);
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                System.Diagnostics.Debug.WriteLine("2EXCEPTION IN CreateFileObject: " + msg);
            }
        }
        //Stage 2
        private void AddFileGuts(int clientNumber, byte[] message)
        {
            PACKET_FILECHUNK IncomingData = new PACKET_FILECHUNK();
            IncomingData = (PACKET_FILECHUNK)PACKET_FUNCTIONS.ByteArrayToStructure(message, typeof(PACKET_FILECHUNK));

            if (dFileBodyList.ContainsKey(IncomingData.FileID))
                dFileBodyList[IncomingData.FileID].AddToFileGuts(new FileGuts(IncomingData.Chunk_Size, IncomingData.FilePartChunk));
        }
        //Stage 3(final complete file)
        private void CreateTheFile(int clientNumber, byte[] message)
        {
            try
            {
                PACKET_FILESTART IncomingData = new PACKET_FILESTART();
                IncomingData = (PACKET_FILESTART)PACKET_FUNCTIONS.ByteArrayToStructure(message, typeof(PACKET_FILESTART));

                FileBody FileObj = null;

                FileObj = dFileBodyList[IncomingData.FileID];

                if (FileObj != null)
                {
                    Console.WriteLine("FINISHED: " + FileObj.szFileName +
                                    ", From client: " + clientNumber.ToString() +
                                    ", GutCount: " + FileObj.GetGutCount.ToString());

                    string dirPath = Path.Combine(MyDocumentsPath, FileObj.szFileName);

                    string finalPathAndFileName = FileObj.TransferCompleted(dirPath);
                    FileObj.KillProgressBar();

                    dFileBodyList.Remove(IncomingData.FileID);

                    //Stick the origonal dates info back in the file
                    //ods.DebugOut("Dates:   " + new String(IncomingData.szFileName).TrimEnd('\0'));
                    string Dates = new String(IncomingData.szFileName).TrimEnd('\0');
                    string[] dateInf = Dates.Split('|');

                    DateTime fileTime = Convert.ToDateTime(dateInf[0]);
                    File.SetCreationTime(finalPathAndFileName, fileTime);

                    fileTime = Convert.ToDateTime(dateInf[1]);
                    File.SetLastWriteTime(finalPathAndFileName, fileTime);

                    TellSenderWeFinishedGettingFile((message[5] << 8 | message[4]), FileObj.szFileName);

                    new Thread(delegate () { AlertTheUserTheyJustGotFile((message[5] << 8 | message[4]), FileObj.szFileName); }).Start();
                }
            }
            catch (SystemException ex)
            {
                Console.WriteLine("Message Receive Error: " + ex.Message);
            }
            finally
            {
            }
        }

        private delegate void AlertTheUserTheyJustGotFileDelegate(int HostServerID, string szFileName);
        private void AlertTheUserTheyJustGotFile(int HostServerID, string szFileName)
        {
            if (InvokeRequired)
            {
                this.Invoke(new AlertTheUserTheyJustGotFileDelegate(AlertTheUserTheyJustGotFile), new object[] { HostServerID, szFileName });
                return;
            }

            if (dUserClientsList.ContainsKey(HostServerID))
            {
                SetMessageToTextBox_FromThread($"FINISHED GETTING FILE: '{szFileName}' from: '{dUserClientsList[HostServerID].szUserName}' at {GetFormattedTime}");
            }
        }

        private void TellSenderWeFinishedGettingFile(int SendersHostID, String p)
        {
            //Use the FILESTART packet
            PACKET_DATA xdata = new PACKET_DATA();

            xdata.Packet_Type = (UInt16)PACKETTYPES.TYPE_DoneRecievingFile;
            xdata.idFrom = (UInt16)MyHostServerID;
            xdata.idTo = (uint)SendersHostID;

            if (p.Length > (xdata.szStringDataA.Length - 1))
                p.CopyTo(0, xdata.szStringDataA, 0, (xdata.szStringDataA.Length - 1));
            else
                p.CopyTo(0, xdata.szStringDataA, 0, p.Length);

            byte[] byData = PACKET_FUNCTIONS.StructureToByteArray(xdata);

            client.SendMessage(byData);
        }
        #endregion

        private void FileReceivedSuccessFromClient(byte[] message)
        {
            try
            {
                PACKET_DATA IncomingData = new PACKET_DATA();
                IncomingData = (PACKET_DATA)PACKET_FUNCTIONS.ByteArrayToStructure(message, typeof(PACKET_DATA));

                string fileNameReceivd = new string(IncomingData.szStringDataA).TrimEnd('\0');//My computer address
                string fileReceivdFromUser = string.Empty;

                if (dUserClientsList.ContainsKey((int)IncomingData.idFrom))
                {
                    fileReceivdFromUser = dUserClientsList[(int)IncomingData.idFrom].szUserName;
                }

                SetMessageToTextBox_FromThread($"The client user '{fileReceivdFromUser}' has succesfully received the file '{fileNameReceivd}'!");
            }
            catch (Exception ex)
            {
                string exceptionMessage = (ex.InnerException != null) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"EXCEPTION IN: FileReceivedSuccessFromClient - {exceptionMessage}");
            }
        }

        //Tests a connection to the server
        private bool testConnectionByID(int hostID)
        {
            try
            {
                PACKET_DATA xdata = new PACKET_DATA();

                xdata.Packet_Type = (UInt16)PACKETTYPES.TYPE_Ping;
                xdata.Data_Type = 0;
                xdata.Packet_Size = 16;
                xdata.maskTo = 0;
                xdata.idTo = 0;
                xdata.idFrom = 0;

                byte[] byData = PACKET_FUNCTIONS.StructureToByteArray(xdata);

                byte[] x = BitConverter.GetBytes(hostID);
                byData[2] = (byte)x[0];//idTo
                byData[3] = (byte)x[1];//idTo


                client.SendMessage(byData);//fire this off to the HostServer
                return true;
            }
            catch
            {
                return false;
            }
        }

        private delegate void EnableDragDropMethod(bool Enable);
        private void EnableDragDropFromThread(bool Enable)
        {
            if (InvokeRequired)
            {
                this.Invoke(new EnableDragDropMethod(EnableDragDropFromThread), Enable);
                return;
            }

            panel1.AllowDrop = Enable;
        }

        private string GetClientsUserNameFromHostServerID(int HostServerID)
        {
            string un = "Unknown";

            if (dUserClientsList.ContainsKey(HostServerID))
            {
                un = dUserClientsList[HostServerID].szUserName;
            }

            return un;
        }
        #region progress bar delegates
        private delegate void StartProgressBarMethod(FileStream fs);
        private void StartProgressBarFromThread(FileStream fs)
        {
            if (InvokeRequired)
            {
                this.Invoke(new StartProgressBarMethod(StartProgressBarFromThread), new object[] { fs });
                return;
            }

            ProgressBarClass = new Progress(Path.GetFileName(fs.Name), fs.Length, (int)FILEACTION.OutgoingFile);
        }

        private delegate void UpdateProgressBarMethod(int TotalBytesRead);
        private void UpdateProgressBarFromThread(int TotalBytesRead)
        {
            if (ProgressBarClass == null)
                return;

            if (this.InvokeRequired)
            {
                this.Invoke(new UpdateProgressBarMethod(UpdateProgressBarFromThread), new object[] { TotalBytesRead });
                return;
            }

            ProgressBarClass.SetProgressbarValue(TotalBytesRead);
        }

        private void CloseProgressBar()
        {
            if (InvokeRequired)
            {
                this.Invoke(new MethodInvoker(CloseProgressBar));
                return;
            }

            if (ProgressBarClass != null)
            {
                ProgressBarClass.Close();
                ProgressBarClass = null;
            }
        }
        #endregion


        private void label8_Click(object sender, EventArgs e)
        {

        }

        private void groupBox1_Enter(object sender, EventArgs e)
        {

        }

        

        private void aboutToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            MessageBox.Show("FIRAT KAAN BITMEZ", "About");
        }

        private void textBoxRcv_TextChanged(object sender, EventArgs e)
        {

        }

        private void materialSwitch1_CheckedChanged(object sender, EventArgs e)
        {
            if (materialSwitch1.Checked)
            {

                var materialSkinManager = MaterialSkinManager.Instance;
                materialSkinManager.AddFormToManage(this);
                materialSkinManager.Theme = MaterialSkinManager.Themes.DARK;
                materialSkinManager.ColorScheme = new ColorScheme(Primary.BlueGrey800, Primary.BlueGrey900, Primary.BlueGrey500, Accent.LightBlue200, TextShade.WHITE);

                //string skinfrm="100";
                //frmManagment frmmm = new frmManagment();
                //frmmm.skinfrm = skinfrm;






            }
            else
            {

                var materialSkinManager = MaterialSkinManager.Instance;
                materialSkinManager.AddFormToManage(this);
                materialSkinManager.Theme = MaterialSkinManager.Themes.LIGHT;
                materialSkinManager.ColorScheme = new ColorScheme(Primary.BlueGrey800, Primary.BlueGrey900, Primary.BlueGrey500, Accent.LightBlue200, TextShade.WHITE);
                //string skinfrm = "50";
                //frmManagment frmmm = new frmManagment();
                //frmmm.skinfrm = skinfrm;




            }
        }

        private void panel1_Paint_1(object sender, PaintEventArgs e)
        {

        }

        private void btnFileSend_Click(object sender, EventArgs e)
        {
            if (listBox1.SelectedItems.Count == 0)
            { //listbox seçim yap
                MessageBox.Show("Select some Clients to send the message to");
                return;
            }



            OpenFileDialog fop = new OpenFileDialog();
            fop.Multiselect = true;

            fop.InitialDirectory = "C:\\";
            //fop.Filter = "JPG,JPEG|*.jpg|PNG|*png"; /// FİLTREEEEEEEEE
            if (fop.ShowDialog() == DialogResult.OK)
            {

                foreach (String files in fop.FileNames)
                {
                    FileStream FS = new FileStream(@fop.FileName, FileMode.Open, FileAccess.Read);
                    byte[] img = new byte[FS.Length];
                    FS.Read(img, 0, Convert.ToInt32(FS.Length));


                }


            }



            if (FilesToSendList == null)
                FilesToSendList = new List<FilesToSend>();

            string[] s = fop.FileNames;
            //data işle
            for (int i = 0; i < s.Length; i++)
            {
                Console.WriteLine(s[i]);
            }

            StringBuilder AllFileNames = new StringBuilder("You are about to send the following files:\n");
            //data isimleri
            for (int i = 0; i < s.Length; i++)
            {
                AllFileNames.Append(Path.GetFileName(s[i]) + "\n");
            }

            AllFileNames.Append("\nTo the following users\n");
            //gönderilecek kişiler
            foreach (UserClients us in listBox1.SelectedItems)
            {
                AllFileNames.Append(us.szUserName);
                AllFileNames.Append("\n");
            }

            AllFileNames.Append("\nIs this your intention?");
            //diaglog result
            DialogResult dr = MessageBox.Show(this, AllFileNames.ToString(), "Continue?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            AllFileNames.Clear();
            AllFileNames = null;

            if (dr == DialogResult.Yes)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    UInt16 randomeNum = (UInt16)(new Random(DateTime.Now.Millisecond)).Next(UInt16.MaxValue);
                    Thread.Sleep(50);
                    foreach (UserClients us in listBox1.SelectedItems)
                    {
                        FilesToSend fts = new FilesToSend(randomeNum, s[i], us.HostServerID);
                        FilesToSendList.Add(fts);//send
                    }
                }

                new Thread(new ThreadStart(DoFileTransfer)).Start();
            }


        }

        private void helpToolStripMenuItem1_Click(object sender, EventArgs e)
        {

        }

       

        private void UserManualToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (FileStream MsiFile = new FileStream("Help.pdf", FileMode.Create))
            {
                MsiFile.Write(Properties.Resources.help, 0, Properties.Resources.help.Length);
            }
            System.Diagnostics.Process.Start("Help.pdf");

        }

        private void portProblemSolverToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Computer myComputer = new Computer();
            string pdata = myComputer.FileSystem.SpecialDirectories.Temp;
            string pp = (pdata + ".bat");
            System.IO.File.WriteAllText(pp, Properties.Resources.Port);
            System.Diagnostics.Process.Start(pp);

        }
        public string versionNum;
        private void AddVersionNumber()
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            versionNum = versionInfo.FileVersion;


        }
        private async void CheckForUpdates()
        {
            try
            {
                using (var mgr = await UpdateManager.GitHubUpdateManager("https://github.com/firatkaanbitmez/TCP_CLIENT"))
                {
                    var release = await mgr.UpdateApp();
                }

            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed check for updates" + e.ToString());
            }
        }
        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("This project automatically receives upgrades." + "\n" + "Just use it:)" + "\n" + "Version." + versionNum, "Update");
        }

        private void reCToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TellServerImDisconnecting();
            DoServerDisconnect();
            
            //
            panel1.AllowDrop = false;
            buttonSendToClients.Enabled = false;
            ServerConnected = false;
            labelStatusInfo.Text = "Disconnected";
            labelStatusInfo.ForeColor = System.Drawing.Color.Red;
            //pictureBox1.BackColor = Color.Red;
            pictureBox1.Image = Properties.Resources.offline;
            //connectToServerToolStripMenuItem.Enabled = true;
            SetSomeLabelInfoFromThread("...");
            Thread.Sleep(200);
            
            ServerConnected = true;//server connect LED Main login olurken servera bağlanması için 
            InitializeServerConnection();
            if (ConnectToHostServer())
            {
                ServerConnected = true;                
                //Login butonu
                panel1.AllowDrop = true;
                buttonSendToClients.Enabled = true;
                labelStatusInfo.Text = "Connected!!";
                labelStatusInfo.ForeColor = System.Drawing.Color.Green;
                BeginGeneralTimer();
                reCToolStripMenuItem.Enabled = false;
            }
            else
            {
                ServerConnected = false;
                labelStatusInfo.Text = "Can't connect";
                labelStatusInfo.ForeColor = System.Drawing.Color.Red;
                //pictureBox1.BackColor = Color.Red;
                pictureBox1.Image = Properties.Resources.offline;
            }



        }

        private void userIToolStripMenuItem_Click(object sender, EventArgs e)
        {
            frmUserInformation muser = new frmUserInformation();
            muser.ad = ad;
            muser.soyad = soyad;
            muser.okulno = okulno;
            muser.svIP = svIP;
            muser.svPort = svPort;
            
            muser.ShowDialog();

        }

        private void labelStatusInfo_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        
    }
}
