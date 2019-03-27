import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class CookieService {
  constructor() { }


  public get(): Record<string, string>;
  public get(key: string): string | undefined;
  public get(key?: string): string | undefined | Record<string, string> {
    var allCookies = document.cookie.split(";").map(c => {
      const equalIndex = c.indexOf("=");
      const key = decodeURIComponent(c.substring(0, equalIndex)).trim();
      const value = decodeURIComponent(c.substring(equalIndex + 1)).trim();
      return {key, value};
    });
    if (key) {
      const cookie = allCookies.find(c => c.key === key);
      if (cookie) {
        return cookie.value;
      }
      return undefined;
    }

    const result: Record<string, string> = {};
    for (const cookie of allCookies) {
      result[cookie.key] = cookie.value;
    }
    return result;
  }

  public set(key: string, value: string) {
    key = encodeURIComponent(key);
    value = encodeURIComponent(value);
    document.cookie = `${key}=${value};path=/;samesite=lax;max-age=${100*365*24*60*60 /* 100 years */}`;
  }
}
