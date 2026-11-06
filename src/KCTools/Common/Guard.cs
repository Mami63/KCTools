using System;
using System.Threading.Tasks;
using KCTools.Services;

namespace KCTools.Common
{
    /// <summary>
    /// Kritik UI/USB olaylarını saran hata yakalayıcı. Amaç: kablo çekildiğinde,
    /// süreç öldüğünde veya sürücü hata verdiğinde uygulamanın çökmemesi.
    /// </summary>
    public static class Guard
    {
        public static void Run(Action action, string context)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                Report(context, ex);
            }
        }

        public static T Run<T>(Func<T> action, string context, T fallback = default)
        {
            if (action == null)
            {
                return fallback;
            }

            try
            {
                return action();
            }
            catch (Exception ex)
            {
                Report(context, ex);
                return fallback;
            }
        }

        public static async Task RunAsync(Func<Task> action, string context)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Report(context, ex);
            }
        }

        private static void Report(string context, Exception exception)
        {
            var message = exception is AggregateException aggregate
                ? aggregate.GetBaseException().Message
                : exception.Message;

            LogHub.Instance.Error((context ?? "İşlem") + " hatası: " + message, "GUARD");
        }
    }
}
