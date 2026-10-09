using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace SCI_PH.PatientPrototype.Features.Patient.Ai;

public partial class AiAssistantPage : ContentPage
{
    public AiAssistantPage()
    {
        InitializeComponent();
    }

    private void OnSendClicked(object sender, EventArgs e)
    {
        string question = QuestionEditor.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(question))
            return;

        AddUserMessage(question);
        AddAssistantMessage(BuildEducationalAnswer(question));

        QuestionEditor.Text = string.Empty;
    }

    private void AddUserMessage(string text)
    {
        MessagesContainer.Children.Add(
            new Border
            {
                BackgroundColor = Color.FromArgb("#1976D2"),
                Stroke = Color.FromArgb("#1976D2"),
                StrokeThickness = 1,
                Padding = 18,
                HorizontalOptions = LayoutOptions.End,
                MaximumWidthRequest = 700,

                StrokeShape = new RoundRectangle
                {
                    CornerRadius = 22
                },

                Content = new Label
                {
                    Text = text,
                    FontSize = 18,
                    TextColor = Colors.White,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            });
    }

    private void AddAssistantMessage(string text)
    {
        MessagesContainer.Children.Add(
            new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#D7E1EC"),
                StrokeThickness = 1,
                Padding = 18,
                HorizontalOptions = LayoutOptions.Start,
                MaximumWidthRequest = 700,

                StrokeShape = new RoundRectangle
                {
                    CornerRadius = 22
                },

                Content = new Label
                {
                    Text = text,
                    FontSize = 18,
                    TextColor = Color.FromArgb("#243B5A"),
                    LineBreakMode = LineBreakMode.WordWrap
                }
            });
    }

    private string BuildEducationalAnswer(string question)
    {
        string normalized = question.ToLowerInvariant();

        if (normalized.Contains("glucosa"))
        {
            return "La glucosa es el nivel de azúcar en sangre. Un valor como 92 mg/dL suele considerarse orientativamente dentro de rangos frecuentes de normalidad en muchas circunstancias, pero su interpretación final depende del contexto clínico y de la indicación médica.";
        }

        if (normalized.Contains("presión") ||
            normalized.Contains("presion"))
        {
            return "La presión arterial refleja la fuerza con la que la sangre circula por las arterias. Para interpretar el valor correctamente hay que revisar el resultado completo y el contexto de salud del paciente.";
        }

        if (normalized.Contains("hemoglobina"))
        {
            return "La hemoglobina es una proteína de la sangre que transporta oxígeno. Su significado depende del valor reportado, la edad, el sexo y el contexto clínico.";
        }

        if (normalized.Contains("plaquetas"))
        {
            return "Las plaquetas participan en la coagulación. Si tienes un resultado específico, puedo ayudarte a entender de forma general qué mide ese examen.";
        }

        return "Puedo darte una explicación educativa del término o resultado que escribiste. Recuerda que esta orientación no sustituye la evaluación de un profesional de salud.";
    }
}