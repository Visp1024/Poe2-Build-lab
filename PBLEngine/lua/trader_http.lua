-- PBL trader HTTP bridge: реализует launch:DownloadPage поверх C# HttpClient.
-- C# регистрирует функцию PBL_HttpStart(id, url, header, body) и доставляет
-- ответы вызовом _pblHttpComplete на Lua-потоке (LuaHost.DrainTraderHttp).
_pblHttp = { nextId = 1, pending = {} }

function launch:DownloadPage(url, callback, params)
	params = params or {}
	local id = _pblHttp.nextId
	_pblHttp.nextId = id + 1
	_pblHttp.pending[id] = callback
	PBL_HttpStart(id, url, params.header, params.body)
end

function _pblHttpComplete(id, body, header, errMsg)
	local cb = _pblHttp.pending[id]
	_pblHttp.pending[id] = nil
	if cb then
		cb({ body = body, header = header }, errMsg)
	end
end
