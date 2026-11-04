// Deliberately insecure, non-executable input for demonstrating the syntax rules.
// This file is outside the application project and is never built or executed.
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

class VulnerableExample
{
    private string apiKey = "demo-not-a-real-secret"; // VT001

    void Example(string userInput)
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true; // VT002
        var hash = MD5.Create(); // VT003
        var command = new SqlCommand("SELECT * FROM Users WHERE Name = '" + userInput + "'"); // VT004
        var cipher = DES.Create(); // VT005
    }
}
