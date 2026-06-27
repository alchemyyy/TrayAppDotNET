const VERSION_ASSET_PATH = "/versions.xml";
const NOT_FOUND_RESPONSE_BODY = "Not found";

export default {
  fetch(request: Request, environment: Env): Promise<Response> | Response {
    const requestUrl: URL = new URL(request.url);
    if (requestUrl.pathname !== "/") {
      return new Response(NOT_FOUND_RESPONSE_BODY, {
        status: 404,
        headers: { "Content-Type": "text/plain; charset=utf-8" },
      });
    }

    requestUrl.pathname = VERSION_ASSET_PATH;
    return environment.ASSETS.fetch(new Request(requestUrl.toString(), request));
  },
} satisfies ExportedHandler<Env>;
