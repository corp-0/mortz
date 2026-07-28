FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends libssl3 \
    && rm -rf /var/lib/apt/lists/*
RUN useradd --create-home mortz
WORKDIR /home/mortz
# Skips the ~40 MB libicu dep, the server never shows localized text.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
# Exporting from Windows loses the exec bit, and a RUN chmod would copy the
# 142 MB binary into a second layer.
COPY --chown=mortz:mortz --chmod=755 MortzServer.x86_64 ./
COPY --chown=mortz:mortz data_Mortz_linuxbsd_x86_64 ./data_Mortz_linuxbsd_x86_64
COPY --chown=mortz:mortz content ./content
USER mortz
EXPOSE 7777/udp
# Server-browser queries (game port + 1).
EXPOSE 7778/udp
ENTRYPOINT ["./MortzServer.x86_64", "--headless", "++"]
