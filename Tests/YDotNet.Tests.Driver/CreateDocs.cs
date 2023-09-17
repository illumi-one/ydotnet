using YDotNet.Document;

namespace YDotNet.Tests.Driver;

public class CreateDocs
{
    public void Run()
    {
        var count = 0;

        // Create 1 million documents
        while (count < 1_000_000)
        {
            // After 1s, stop and show the user the amount of documents
            if (count > 0)
            {
                Console.WriteLine("Status Report");
                Console.WriteLine($"\tDocuments:\t{count}");
                Console.WriteLine();
            }

            // Create many documents
            for (var i = 0; i < 100; i++)
            {
                new Doc().Dispose();
                count++;
            }
        }
    }
}
