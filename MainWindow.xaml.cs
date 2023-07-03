using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.IO;
using System.IO.Packaging;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic;
using QRCoder;
using static System.Net.WebRequestMethods;
using static QRCoder.PayloadGenerator;

namespace VRP_POC_VirtualPrinter;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{

    public string sUrlConnection { get; set; }
    public string sRESTConnection { get; set; }

    public string sMessageToResend { get; set; }
    string clientID { get => edtPrinterIdentifier.Text; }
    private string ClearJson { get; set; }

    HubConnection connection;
    HttpClient client;




    public MainWindow()
    {
        InitializeComponent();
        sUrlConnection = "http://localhost:5130/connectort";
        sRESTConnection = "http://localhost:5130/api/receipt";
        client = new HttpClient();
        RegisterSignalRConnection();
        messages.Document = new FlowDocument();
    }

    private async Task connection_Reconnected(string? arg)
    {
        this.Dispatcher.Invoke(() =>
        {
            var newMessage = "SIGNAL R:  Reconnect to " + sUrlConnection;
            LogDebugInfo(newMessage);
        });
        await connection.InvokeAsync<string>("RegisterClient", edtPrinterIdentifier.Text);

        this.Dispatcher.Invoke(() =>
        {
            var newMessage = $"SIGNAL R: Client Again registered as POS {edtPrinterIdentifier.Text} on " + sUrlConnection;
            LogDebugInfo(newMessage);
        });
    }


    private Boolean RegisterSignalRConnection()
    {
        //messages here 
        connection = new HubConnectionBuilder()
            .WithUrl(url: sUrlConnection)
            .WithAutomaticReconnect()
            .Build();

        connection.Reconnecting += (sender) =>
        {
            this.Dispatcher.Invoke(() =>
            {
                var newMessage = "SIGNAL R: Attempting to reconnect to" + sUrlConnection;
                LogDebugInfo(newMessage);
            });
            return Task.CompletedTask;
        };

        connection.Reconnected += connection_Reconnected;

        connection.Closed += (sender) =>
        {
            this.Dispatcher.Invoke(() =>
            {
                var newMessage = "SIGNAL R: Connection closed " + sUrlConnection;
                LogDebugInfo(newMessage);
            });
            return Task.CompletedTask;
        };
        return connection.State == HubConnectionState.Connected;
    }


    private async Task<bool> SendReceipt(string strPayload)
    {
        LogDebugInfo($"REST : Sending json payload to: {sRESTConnection}");
        var response = await client.PostAsync( sRESTConnection, new StringContent(strPayload, Encoding.UTF8, "application/json"));
        LogDebugInfo($"REST : Awaiting response from: {sRESTConnection}");
        var contents = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            LogMainInfo("REST :-< Response (200):" + contents);
            return true;
        }
        else
        {
            LogErrorInfo("REST :-< Response (FAILED)");
            return false;
        }
    }
    private async Task<bool> ConnectToSignalR()
    {
        connection.On<string>(methodName: "ReceiveMessage", (message) =>
        {
            this.Dispatcher.Invoke(() =>
            {
                var newMessage = $"SIGNAL R: -< Confirmation{message}";
                LogMainInfo(newMessage);
                imgQrCode.Source = GenerateQrCode(message);
            });
        });
        try
        {
            await connection.StartAsync();
            if (connection.State == HubConnectionState.Connected)
            {
                LogMainInfo("SIGNAL R: Connected to Signal R ");
                connection.InvokeAsync<string>("RegisterClient", edtPrinterIdentifier.Text).Wait();
                return true;
            }
            else
            {
                LogErrorInfo("SIGNAL R: Failed to connect");
                return false;
            }
        }
        catch (Exception ex)
        {
            LogErrorInfo(ex.Message);
            return false;
        }
    }

    private string GetDefaultReceipt(bool bPreetyPrint = false)
    {
        var payload = new Dictionary<string, object>
        {
            {"TranactionNmbr", 1},
            {"StoreNmbr", 1},
            {"PosNmbr", clientID},
            {"CashierName", "Cashier 1"}
        };

        if (bPreetyPrint)
        {
            JsonSerializerOptions options = new()
            {
                WriteIndented = true
            };
            return (JsonSerializer.Serialize(payload, options));
        }
        else
            return (JsonSerializer.Serialize(payload));
    }


    private string AddTimeStamp(string inStr)
    {
        return ($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} " + inStr);
    }

    private void LogDebugInfo(string sMessage)
    {
        Paragraph myParagraph = new Paragraph();
        myParagraph.Inlines.Add(new Run(AddTimeStamp(sMessage))
        {
            Foreground = System.Windows.Media.Brushes.Blue
        });
        messages.Document.Blocks.Add(myParagraph);
        messages.ScrollToEnd();

    }

    private void LogErrorInfo(string sMessage)
    {
        //        messages.Items.Add(AddTimeStamp(inStr: sMessage));
        Paragraph myParagraph = new Paragraph();
        myParagraph.Inlines.Add(new Bold(new Run(AddTimeStamp(sMessage)))
        {
            Foreground = System.Windows.Media.Brushes.Red
        });
        messages.Document.Blocks.Add(myParagraph);
        messages.ScrollToEnd();

    }
    private void LogMainInfo(string sMessage)
    {
        Paragraph myParagraph = new Paragraph();
        myParagraph.Inlines.Add(new Bold(new Run(AddTimeStamp(sMessage)))
        {
            Foreground = System.Windows.Media.Brushes.Green
        });
        messages.Document.Blocks.Add(myParagraph);
        messages.ScrollToEnd();
    }

    private async void btnReceipt_1_Click(object sender, RoutedEventArgs e)
    {
        bool IsConnected = await ConnectToSignalR();
        btnConnect.IsEnabled = !IsConnected;
        btnSendRecipt.IsEnabled = IsConnected;
        edtPrinterIdentifier.IsEnabled = !IsConnected;

    }

    private async void btnSendRecipt_Click(object sender, RoutedEventArgs e)
    {
        await SendReceipt(GetDefaultReceipt());
    }

    private void edtPrinterIdentifier_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        tblkReceipt_json.Text = this.GetDefaultReceipt(true);
    }

    private BitmapImage GenerateQrCode(string inResponsePayload)
    {
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(inResponsePayload);
        string sUID = payload["_QRcode"].ToString();

        QRCodeGenerator qrGenerator = new QRCodeGenerator();
        QRCodeData qrCodeData = qrGenerator.CreateQrCode(sUID, QRCodeGenerator.ECCLevel.Q);
        QRCode qrCode = new QRCode(qrCodeData);
        return BitmapToImageSource(qrCode.GetGraphic(20));
    }


    private BitmapImage BitmapToImageSource(Bitmap bitmap)
    {
        using (MemoryStream memory = new MemoryStream())
        {
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
            memory.Position = 0;
            BitmapImage bitmapimage = new BitmapImage();
            bitmapimage.BeginInit();
            bitmapimage.StreamSource = memory;
            bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapimage.EndInit();

            return bitmapimage;
        }
    }
};
