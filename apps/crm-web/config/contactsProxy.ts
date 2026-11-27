const uuid = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}';
const contact = '/api/v1/contacts/' + uuid;
const routes: [RegExp, string[]][] = [
  [/^\/api\/v1\/(?:contacts|tags|custom-fields)$/, ['GET', 'POST']],
  [new RegExp('^' + contact + '$'), ['GET', 'PATCH', 'DELETE']],
  [new RegExp('^' + contact + '/(?:tags|timeline)$'), ['GET']],
  [new RegExp('^' + contact + '/notes$'), ['POST']],
  [new RegExp('^' + contact + '/tags/' + uuid + '$'), ['PUT', 'DELETE']],
  [new RegExp('^' + contact + '/fields/' + uuid + '$'), ['GET', 'PUT']]
];

export function contactMethods(url: string) {
  const path = url.split('?', 1)[0]!;
  return routes.find(([pattern]) => pattern.test(path))?.[1];
}
