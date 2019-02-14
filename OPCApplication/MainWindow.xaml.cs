using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Services;
using CommunicationServers.OPC;
using OPCAutomation;
using Services.DataBase;
using System.Data;
using OPCApplication.Models;
using CommunicationServers.Sockets;
using ProtocolFamily.Modbus;
using System.Windows.Threading;
using System.Threading;

namespace OPCApplication
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        SqlHelper sql = new SqlHelper();
        AccessHelper access = new AccessHelper();
        OPCClientHelper OPCClient;
        SocketServer SocketServer = new SocketServer();
        IniHelper ini = new IniHelper(System.AppDomain.CurrentDomain.BaseDirectory + @"\Set.ini");
        CharacterConversion characterConversion;
        List<OpcListModel> models;
        string ServerIP = "127.0.0.1";
        OPCServer opcServer;
        bool blConnectOPC = false;
        DispatcherTimer ReConnectToOpcTimer = new DispatcherTimer();
        public delegate void MethodInvoker(List<OPCDataItem> OpcDataItems);
        bool Restart = false;
        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ReConnectToOpcTimer.Interval = TimeSpan.FromSeconds(60);
            ReConnectToOpcTimer.Tick += ReConnectToOpcTimer_Tick;
            ReConnectToOpcTimer.Start();
            IniOpc();
            AddOpcTags();
            iniSocket();
            this.WindowState = WindowState.Minimized;
        }

        private void ReConnectToOpcTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if(Restart)
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    Restart = true;
                }
            }
            catch(Exception ex)
            {
                SimpleLogHelper.Instance.WriteLog(LogType.Error, ex);
            }
        }

        private void  iniSocket()
        {
            if (this.SocketServer.Listen(ini.ReadIni("Config","Port")))
            {
                this.SocketServer.NewMessage1Event += SocketServer_NewMessage1Event;
            }
        }

        private void SocketServer_NewMessage1Event(System.Net.Sockets.Socket socket, string Message)
        {
            try
            {
                if(this.opcServer.ServerState == (int)OPCServerState.OPCRunning)
                {
                    ModbusTcpServer modbusTcpServer = new ModbusTcpServer();
                    modbusTcpServer.AffairID = Message.Substring(0, 4);
                    modbusTcpServer.ProtocolID = Message.Substring(4, 4);
                    int requestDataLength = MathHelper.HexToDec(Message.Substring(Message.Length - 4, 4)); //请求数据长度
                    modbusTcpServer.BackDataLength = MathHelper.DecToHex((requestDataLength * 2).ToString()).PadLeft(2, '0');
                    modbusTcpServer.SlaveId = Message.Substring(12, 2);
                    modbusTcpServer.Length = MathHelper.DecToHex((3 + requestDataLength * 2).ToString()).PadLeft(4, '0');
                    string backdata = modbusTcpServer.AffairID + modbusTcpServer.ProtocolID + modbusTcpServer.Length + modbusTcpServer.SlaveId + ModbusFunction.ReadHoldingRegisters + modbusTcpServer.BackDataLength;
                    if (models.Count < requestDataLength / 2) return;
                    for (int i = 0; i < requestDataLength / 2; i++)
                    {
                        string a = MathHelper.SingleToHex(models[i].Value);
                        backdata += a.Substring(4, 4) + a.Substring(0, 4);
                    }
                    characterConversion = new CharacterConversion();
                    this.SocketServer.Send(socket, characterConversion.HexConvertToByte(backdata));
                }
                else
                {
                    if(ReConnectToOpcTimer.IsEnabled == false)
                    {
                        ReConnectToOpcTimer.Start();
                    }
                }
            }
            catch(Exception ex)
            {
                if (ReConnectToOpcTimer.IsEnabled == false)
                {
                    ReConnectToOpcTimer.Start();
                }
                SimpleLogHelper.Instance.WriteLog(LogType.Error, ex);
            }
        }

        /// <summary>
        /// opc初始化
        /// </summary>
        private void IniOpc()
        {
            //Kepware.KEPServerEX.V5
            //KingView.View.1
            string name = "KingView.View.1";
            OPCClient = new OPCClientHelper();
            List<string> names = new List<string>();
            OPCClient.DataChangeEvent += OPCClient_DataChangeEvent;
            //names = OPCClient.GetOPCServerNames(ServerIP);
            //opcServer = OPCClient.ConnectToServer(names[0], ServerIP);
            opcServer = OPCClient.ConnectToServer(name, ServerIP);
            OPCBrowser opcbrowser = OPCClient.RecurBrowse(opcServer);
            foreach (var item in opcbrowser)
            {
                this.lstTags.Items.Add(item.ToString());
            }
            OPCClient.CreateGroup(opcServer, "tianchen");
        }

        /// <summary>
        /// opc添加标签
        /// </summary>
        private void AddOpcTags()
        {
            models = access.GetDataTable<OpcListModel>("Select * from [OPCConfig] order by Id asc");
            foreach (var item in models)
            {
                OPCClient.AddItem(item.Name);
                this.lstAddedTags.Items.Add(item.Name);
            }
        }

        private  void OPCClient_DataChangeEvent(List<OPCDataItem> OpcDataItems)
        {
            try
            {
                Restart = false;
                foreach (var item in OpcDataItems)
                {
                    if (item.ItemValue != null)
                    {
                        //右侧显示被选中opc的值
                        if (!string.IsNullOrEmpty(this.opcInfo.Opcname))
                        {
                            if (item.ItemName.ToString() == this.opcInfo.Opcname)
                            {
                                this.opcInfo.OpcValue = Convert.ToSingle(item.ItemValue).ToString();
                            }
                        }

                        int index = models.FindIndex(m => m.Name == item.ItemName.ToString());
                        if (index != -1)
                        {
                            if (item.ItemValue != null)
                            {
                                models[index].Value = Convert.ToSingle(item.ItemValue).ToString();
                            }
                        }
                        //string commandtext = string.Format("UPDATE [OPCConfig] SET [Value]={0} WHERE Name='{1}'", Convert.ToSingle(item.ItemValue).ToString(), item.ItemName);
                        //access.Execute(commandtext);
                    }
                }
            }
            catch (Exception ex)
            {

            }
        }

        private void lstTags_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                string selectedText = (sender as ListBox).SelectedItem.ToString();
                string commandtext = string.Format("INSERT INTO [OPCConfig] (Name) values ('{0}')", selectedText);
                if (access.Execute(commandtext))
                {
                    this.lstAddedTags.Items.Add(selectedText);
                    OPCClient.AddItem(selectedText);
                }
            }
            catch
            {

            }
        }

        private void btnAddressSerial_Click(object sender, RoutedEventArgs e)
        {
            models = access.GetDataTable<OpcListModel>("Select * from [OPCConfig] order by Id asc");
            int addr = 0;
            foreach (var item in models)
            {
                string commandtext = string.Format("UPDATE [OPCConfig] SET [Address]={0} WHERE Name='{1}'", addr.ToString(), item.Name);
                if(access.Execute(commandtext))
                {
                    addr += 2;
                }
                else
                {
                    MessageBox.Show("序列化失败");
                    return;
                }
            }
            MessageBox.Show("序列化成功");
        }

        private void lstAddedTags_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if((sender as ListBox).SelectedItem != null)
            {
                string selectedText = (sender as ListBox).SelectedItem.ToString();
                this.opcInfo.Opcname = selectedText;
                this.opcInfo.Refresh();
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            string name = this.lstAddedTags.SelectedItem.ToString();
            this.lstAddedTags.Items.Remove(this.lstAddedTags.SelectedItem);
            string commandtext = string.Format("Delete from [OPCConfig] WHERE [Name]='{0}'", name);
            Console.WriteLine(access.Execute(commandtext));
        }

        private void CheckOpc_Click(object sender, RoutedEventArgs e)
        {
            if(this.CheckOpc.IsChecked == true)
            {
                if(!ReConnectToOpcTimer.IsEnabled)
                {
                    ReConnectToOpcTimer.Start();
                    this.CheckOpc.IsChecked = false;
                }
            }
            else
            {
                ReConnectToOpcTimer.Stop();
            }
        }
    }
}
