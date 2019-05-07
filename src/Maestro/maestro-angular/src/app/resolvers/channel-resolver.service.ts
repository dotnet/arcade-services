import { Injectable } from '@angular/core';
import { Resolve, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { Channel } from 'src/maestro-client/models';
import { Observable, of } from 'rxjs';
import { ChannelService } from '../services/channel.service';
import { map } from 'rxjs/operators';

@Injectable({
  providedIn: 'root'
})
export class ChannelResolverService implements Resolve<Channel | undefined> {
  constructor(private channelService: ChannelService) { }

  resolve(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Observable<Channel | undefined> {
    const channelId = route.paramMap.get("channelId");
    if (channelId) {
      console.log(`resolving channel ${channelId}`);
      return this.channelService.getChannels().pipe(
        map(channels => channels.find(c => c.id === +channelId)),
      );
    }
    return of(undefined);
  }
}
