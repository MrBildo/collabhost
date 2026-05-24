// UAT executable fixture - tiny Go HTTP server.
//
// Reads $PORT as a bare integer (runbook §3.1 port-injection wire shape).
// Serves HTTP 200 on `/` and `/health`. Exits cleanly on SIGTERM (Linux) /
// CTRL_BREAK (Windows) within ~5s.
//
// Stdlib only - no module deps, fully offline-buildable. `-trimpath` and a
// pinned `-buildvcs=false` keep the output deterministic for the same
// toolchain version on the same machine.
package main

import (
	"context"
	"fmt"
	"log"
	"net/http"
	"os"
	"os/signal"
	"strconv"
	"syscall"
	"time"
)

func main() {
	portRaw := os.Getenv("PORT")
	port, err := strconv.Atoi(portRaw)
	if err != nil || port <= 0 {
		log.Fatalf("[uat-fixture] invalid PORT: %q", portRaw)
	}

	name := "uat-executable"
	if len(os.Args) > 0 {
		name = os.Args[0]
	}

	mux := http.NewServeMux()
	mux.HandleFunc("/health", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/plain")
		_, _ = fmt.Fprintln(w, "ok")
	})
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/plain")
		_, _ = fmt.Fprintf(w, "UAT executable fixture (%s) - PORT=%d\n", name, port)
	})

	addr := fmt.Sprintf(":%d", port)
	srv := &http.Server{
		Addr:              addr,
		Handler:           mux,
		ReadHeaderTimeout: 5 * time.Second,
	}

	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, syscall.SIGTERM, syscall.SIGINT)
	go func() {
		s := <-sigCh
		log.Printf("[uat-fixture] received %v, shutting down", s)
		ctx, cancel := context.WithTimeout(context.Background(), 4*time.Second)
		defer cancel()
		_ = srv.Shutdown(ctx)
	}()

	log.Printf("[uat-fixture] listening on PORT=%d", port)
	if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
		log.Fatalf("[uat-fixture] server error: %v", err)
	}
}
