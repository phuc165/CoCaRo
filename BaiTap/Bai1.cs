using System;

class Bai1
{
    static void Main(string[] args)
    {
        Console.WriteLine("Nhập số thực thứ nhất:");
        double num1 = Convert.ToDouble(Console.ReadLine());

        Console.WriteLine("Nhập số thực thứ hai:");
        double num2 = Convert.ToDouble(Console.ReadLine());

        Console.WriteLine("Nhập số thực thứ ba:");
        double num3 = Convert.ToDouble(Console.ReadLine());

        double max = FindMax(num1, num2, num3);
        Console.WriteLine("Số lớn nhất trong ba số là: " + max);
    }

    static double FindMax(double a, double b, double c)
    {
        double max = a;
        if (b > max)
        {
            max = b;
        }
        if (c > max)
        {
            max = c;
        }
        return max;
    }
}