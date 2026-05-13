<?php

namespace App\Console\Commands;

use App\Jobs\SendOrderJob;
use Illuminate\Console\Command;
use Throwable;

class OrderWorkerCommand extends Command
{
    protected $signature = 'order:worker {--sleep=1 : Seconds to wait before the next polling cycle}';

    protected $description = 'Run a long-lived Redis order queue worker.';

    public function handle(): int
    {
        $sleepSeconds = max(1, (int) $this->option('sleep'));

        $this->info('Order worker started. Press Ctrl+C to stop.');

        while (true) {
            try {
                (new SendOrderJob())->handle();
            } catch (Throwable $exception) {
                $this->error('Order worker loop failed: '.$exception->getMessage());
                report($exception);
            }

            sleep($sleepSeconds);
        }
    }
}
