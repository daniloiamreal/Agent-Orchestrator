Segue um exemplo completo e funcional de uma **calculadora simples em C#**, que pode ser executada no console. Ela permite ao usuário realizar as quatro operações básicas (adição, subtração, multiplicação e divisão), trata divisão por zero e continua rodando até que o usuário decida sair.

```csharp
using System;

class Calculadora
{
    static void Main()
    {
        Console.WriteLine("Calculadora Simples em C#");
        bool continuar = true;

        while (continuar)
        {
            try
            {
                // Entrada do primeiro número
                Console.Write("Digite o primeiro número: ");
                double num1 = Convert.ToDouble(Console.ReadLine());

                // Escolha da operação
                Console.Write("Digite a operação (+, -, *, /): ");
                string operacao = Console.ReadLine();

                // Entrada do segundo número
                Console.Write("Digite o segundo número: ");
                double num2 = Convert.ToDouble(Console.ReadLine());

                double resultado;

                // Execução da operação
                switch (operacao)
                {
                    case "+":
                        resultado = num1 + num2;
                        Console.WriteLine($"Resultado: {resultado}");
                        break;
                    case "-":
                        resultado = num1 - num2;
                        Console.WriteLine($"Resultado: {resultado}");
                        break;
                    case "*":
                        resultado = num1 * num2;
                        Console.WriteLine($"Resultado: {resultado}");
                        break;
                    case "/":
                        if (num2 == 0)
                        {
                            Console.WriteLine("Erro: Divisão por zero não é permitida.");
                        }
                        else
                        {
                            resultado = num1 / num2;
                            Console.WriteLine($"Resultado: {resultado}");
                        }
                        break;
                    default:
                        Console.WriteLine("Operação inválida. Use +, -, * ou /.");
                        break;
                }
            }
            catch (FormatException)
            {
                Console.WriteLine("Entrada inválida. Certifique-se de digitar números válidos.");
            }

            // Pergunta para continuar ou sair
            Console.Write("Deseja realizar outra operação? (s/n): ");
            string resposta = Console.ReadLine().ToLower();
            if (resposta != "s" && resposta != "sim")
            {
                continuar = false;
            }
            Console.WriteLine();
        }

        Console.WriteLine("Obrigado por usar a calculadora. Até logo!");
    }
}
```

**Como funciona:**

- O programa exibe um menu simples no console e solicita dois números e uma operação.
- As operações aceitas são: `+`, `-`, `*` e `/`.
- Caso o usuário tente dividir por zero, uma mensagem de erro é mostrada.
- O programa trata entradas inválidas (não numéricas) com um `try-catch`.
- Após cada operação, pergunta se o usuário deseja continuar.
- O programa só termina quando o usuário responder diferente de "s" ou "sim".

Este código pode ser compilado e executado em qualquer ambiente C# que suporte .NET, como Visual Studio, Visual Studio Code com .NET SDK, ou online em compiladores C#.

Se desejar, posso ajudar a criar uma versão com interface gráfica (Windows Forms, WPF) ou uma calculadora mais avançada.