import { Component, OnInit, Input } from '@angular/core';
import { Asset } from 'src/maestro-client/models';

@Component({
  selector: 'mc-asset-table',
  templateUrl: './asset-table.component.html',
  styleUrls: ['./asset-table.component.scss']
})
export class AssetTableComponent implements OnInit {

  @Input() public assets!: Asset[];
  public page?: number;

  constructor() { }

  ngOnInit() {
  }

}
