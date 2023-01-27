using Microsoft.Extensions.DependencyInjection;
using System;
using MyAutoInjector;

namespace SourceGeneratorTest
{
    class Program
    {
        static void Main(string[] args)
        {
            IServiceCollection sc = new ServiceCollection();

            sc.AutoInject();

            Console.WriteLine("Hello World!");
        }
    }
}
